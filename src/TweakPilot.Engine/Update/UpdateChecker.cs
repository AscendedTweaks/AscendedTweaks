using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using TweakPilot.Core;
using TweakPilot.Engine.Insights;

namespace TweakPilot.Engine.Update;

/// <summary>
/// Feste Update-Einstellungen (Abschnitt 12.3). Adresse, oeffentlicher Schluessel und Herausgeber stehen noch nicht fest
/// (offene Punkte 4 und 7); solange einer fehlt, meldet die Pruefung "nicht eingerichtet" und laedt nichts.
/// Der private Signaturschluessel liegt nie im Repository.
/// </summary>
public static class UpdateConfig
{
    /// <summary>HTTPS-Adresse von manifest.json; die Signatur liegt daneben als manifest.json.sig.</summary>
    public static readonly Uri? ManifestUri = null;

    /// <summary>Oeffentlicher Ed25519-Schluessel (32 Bytes, Base64).</summary>
    public static readonly string? PublicKey = null;

    /// <summary>Erwarteter Herausgeber im Authenticode-Zertifikat des Installers.</summary>
    public static readonly string? ExpectedPublisher = null;
}

/// <summary>Signiertes Manifest; unbekannte Felder werden abgelehnt.</summary>
public sealed record UpdateManifest
{
    public required string Version { get; init; }

    public required Uri InstallerUrl { get; init; }

    /// <summary>SHA-256 des Installers, 64 Hex-Zeichen.</summary>
    public required string Sha256 { get; init; }

    public required long Size { get; init; }

    public string? Notes { get; init; }
}

public enum UpdateCheckOutcome
{
    NotConfigured,
    UpToDate,
    Available,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, UpdateManifest? Manifest, string? Error);

public sealed record UpdateDownload(bool Ok, string? InstallerPath, string? Error);

/// <summary>HTTP-Abruf ohne eigene Kopfzeilen und ohne Kennung; nur HTTPS.</summary>
public interface IUpdateTransport
{
    Task<byte[]> GetAsync(Uri uri, long maxBytes, CancellationToken cancellation);
}

/// <summary>
/// Update nur auf Knopfdruck: Manifest und Signatur laden, Ed25519 pruefen, Version vergleichen. Beim Herunterladen
/// Groesse und SHA-256 aus dem Manifest und die Authenticode-Signatur mit erwartetem Herausgeber pruefen. Gestartet
/// wird der Installer erst nach Bestaetigung in der Oberflaeche.
/// </summary>
public sealed class UpdateChecker
{
    public const long MaxManifestBytes = 64 * 1024;
    public const long MaxInstallerBytes = 512L * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private readonly IUpdateTransport _transport;
    private readonly ISignatureVerifier _authenticode;
    private readonly Uri? _manifestUri;
    private readonly byte[]? _publicKey;
    private readonly string? _publisher;
    private readonly Version _current;
    private readonly bool _currentIsPrerelease;

    public UpdateChecker(IUpdateTransport transport, ISignatureVerifier authenticode, Uri? manifestUri, string? publicKey, string? expectedPublisher, string currentVersion)
    {
        _transport = transport;
        _authenticode = authenticode;
        _manifestUri = manifestUri;
        _publicKey = publicKey is null ? null : Convert.FromBase64String(publicKey);
        _publisher = expectedPublisher;
        // Die eigene Version darf eine Vorabkennung tragen (1.0.0-rc.1); sie ist aelter als dieselbe Version ohne Kennung.
        var core = currentVersion.Split('+')[0];
        _currentIsPrerelease = core.Contains('-', StringComparison.Ordinal);
        _current = ParseVersion(core.Split('-')[0]) ?? new Version(0, 0, 0);
    }

    public bool IsConfigured => _manifestUri is not null && _publicKey is { Length: 32 } && !string.IsNullOrWhiteSpace(_publisher);

    public static bool VerifySignature(byte[] publicKey, byte[] message, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(signature);
        if (publicKey.Length != 32 || signature.Length != 64)
        {
            return false;
        }

        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
            verifier.BlockUpdate(message, 0, message.Length);
            return verifier.VerifySignature(signature);
        }
        catch (ArgumentException)
        {
            // Kein gueltiger Punkt auf der Kurve: wie eine falsche Signatur behandeln.
            return false;
        }
    }

    /// <summary>Nur Versionen der Form 1.2.3 (Semantic Versioning ohne Vorabkennung).</summary>
    public static Version? ParseVersion(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var core = text.Split('+')[0];
        var parts = core.Split('.');
        return parts.Length == 3 && parts.All(p => p.Length is > 0 and <= 6 && p.All(char.IsAsciiDigit))
            ? new Version(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture))
            : null;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellation)
    {
        if (!IsConfigured)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.NotConfigured, null, null);
        }

        try
        {
            if (_manifestUri!.Scheme != Uri.UriSchemeHttps)
            {
                return Failed("The update address is not HTTPS.");
            }

            var manifestBytes = await _transport.GetAsync(_manifestUri, MaxManifestBytes, cancellation).ConfigureAwait(false);
            var signatureText = await _transport.GetAsync(new Uri(_manifestUri + ".sig"), 1024, cancellation).ConfigureAwait(false);
            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(signatureText).Trim());
            }
            catch (FormatException)
            {
                return Failed("The signature of the update manifest is damaged.");
            }

            if (!VerifySignature(_publicKey!, manifestBytes, signature))
            {
                return Failed("The signature of the update manifest is invalid. Nothing is downloaded.");
            }

            UpdateManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestBytes, Options);
            }
            catch (JsonException)
            {
                return Failed("The update manifest has an unknown format.");
            }

            if (manifest is null || ParseVersion(manifest.Version) is not { } version
                || manifest.InstallerUrl.Scheme != Uri.UriSchemeHttps
                || manifest.Sha256.Length != 64 || !manifest.Sha256.All(char.IsAsciiHexDigit)
                || manifest.Size is <= 0 or > MaxInstallerBytes)
            {
                return Failed("The update manifest is incomplete or invalid.");
            }

            return version > _current || (version == _current && _currentIsPrerelease)
                ? new UpdateCheckResult(UpdateCheckOutcome.Available, manifest, null)
                : new UpdateCheckResult(UpdateCheckOutcome.UpToDate, manifest, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidDataException)
        {
            return Failed("The update check failed: " + ex.Message);
        }
    }

    /// <summary>Laedt den Installer in einen eigenen Ordner und prueft Groesse, SHA-256 und Authenticode. Bei jedem Fehler wird die Datei geloescht.</summary>
    public async Task<UpdateDownload> DownloadAsync(UpdateManifest manifest, string directory, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var path = Path.Combine(directory, $"{ProductInfo.Name}-{manifest.Version}-Setup.exe");
        try
        {
            var bytes = await _transport.GetAsync(manifest.InstallerUrl, manifest.Size, cancellation).ConfigureAwait(false);
            if (bytes.LongLength != manifest.Size)
            {
                return new UpdateDownload(false, null, "The download does not have the expected size.");
            }

            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateDownload(false, null, "The checksum of the download does not match. The file was discarded.");
            }

            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(path, bytes, cancellation).ConfigureAwait(false);
            var signature = _authenticode.Verify(path);
            if (signature.Status != SignatureStatus.Valid || !string.Equals(signature.Publisher, _publisher, StringComparison.Ordinal))
            {
                File.Delete(path);
                return new UpdateDownload(false, null, "The digital signature of the installer is invalid or is not from the expected publisher. The file was deleted.");
            }

            return new UpdateDownload(true, path, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidDataException or UnauthorizedAccessException)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return new UpdateDownload(false, null, "The download failed: " + ex.Message);
        }
    }

    private static UpdateCheckResult Failed(string message) => new(UpdateCheckOutcome.Failed, null, message);
}

/// <summary>HTTPS-Abruf mit Groessengrenze. Keine eigenen Kopfzeilen, keine Cookies, keine Kennung.</summary>
public sealed class HttpUpdateTransport : IUpdateTransport, IDisposable
{
    private readonly HttpClient _client = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<byte[]> GetAsync(Uri uri, long maxBytes, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("Only HTTPS is allowed.");
        }

        using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("Redirect to an insecure address.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellation).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidDataException("The response is larger than allowed.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    public void Dispose() => _client.Dispose();
}
