using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace AscendedTweaks.Ipc;

public interface IClientVerifier
{
    /// <summary>Prueft den verbundenen Client. Bei false wird die Verbindung getrennt.</summary>
    bool Verify(NamedPipeServerStream pipe, out string reason);
}

public sealed class ClientVerifierOptions
{
    /// <summary>Prozess-ID, die verbinden darf (der startende Prozess). Null nur in Tests.</summary>
    public int? ExpectedProcessId { get; init; }

    /// <summary>
    /// Das Programm des Clients muss direkt in einem dieser Verzeichnisse liegen (keine Unterordner): dem Ordner des Helfers und,
    /// wenn der Helfer im Unterordner "app" liegt, dem Paketordner darueber (V2 Abschnitt 7.3). Siehe <see cref="DirectoriesFor"/>.
    /// </summary>
    public required IReadOnlyList<string> AllowedDirectories { get; init; }

    public required SecurityIdentifier ExpectedUser { get; init; }

    /// <summary>In Release-Builds Pflicht: gueltige Authenticode-Signatur mit gleichem Herausgeber wie der Helfer.</summary>
    public bool RequireSignature { get; init; }

    public string? HelperImagePath { get; init; }
}

/// <summary>Prueft Prozess-ID, Programmpfad, Benutzer und (in Release-Builds) die Signatur des Clients.</summary>
public sealed class WindowsClientVerifier : IClientVerifier
{
    private readonly ClientVerifierOptions _options;

    public WindowsClientVerifier(ClientVerifierOptions options) => _options = options;

    public bool Verify(NamedPipeServerStream pipe, out string reason)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        if (!PipeNative.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid))
        {
            reason = "Process ID of the client is not readable.";
            return false;
        }

        if (_options.ExpectedProcessId is { } expected && expected != (int)pid)
        {
            reason = $"Unexpected client process {pid} (expected {expected}).";
            return false;
        }

        string? image;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            image = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            reason = $"Client process cannot be checked: {ex.Message}";
            return false;
        }

        if (image is null || !_options.AllowedDirectories.Any(d => IsInDirectory(image, d)))
        {
            reason = $"Client program {image} is not in the installation folder.";
            return false;
        }

        SecurityIdentifier? user = null;
        try
        {
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
                user = identity.User;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = $"User of the client cannot be checked: {ex.Message}";
            return false;
        }

        if (user is null || !user.Equals(_options.ExpectedUser))
        {
            reason = $"Client runs under a different user ({user}).";
            return false;
        }

        if (_options.RequireSignature && !Authenticode.SameTrustedPublisher(image, _options.HelperImagePath ?? Environment.ProcessPath!, out var signatureReason))
        {
            reason = signatureReason;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Erlaubte Client-Ordner fuer einen Helfer in <paramref name="helperDirectory"/>: der Ordner selbst, bei "app" auch der Ordner darueber.</summary>
    public static IReadOnlyList<string> DirectoriesFor(string helperDirectory)
    {
        ArgumentNullException.ThrowIfNull(helperDirectory);
        var own = Path.TrimEndingDirectorySeparator(Path.GetFullPath(helperDirectory));
        return string.Equals(Path.GetFileName(own), "app", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(own) is { } parent
            ? [own, parent]
            : [own];
    }

    internal static bool IsInDirectory(string file, string directory)
    {
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var fileDirectory = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty);
        return string.Equals(fileDirectory, fullDirectory, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class PipeNative
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}

/// <summary>Authenticode-Pruefung ueber WinVerifyTrust plus Vergleich des Herausgeber-Zertifikats.</summary>
[ExcludeFromCodeCoverage(Justification = "Testable only with signed release builds.")]
public static class Authenticode
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static bool IsTrusted(string file)
    {
        var fileInfo = new WinTrustFileInfo(file);
        var data = new WinTrustData(fileInfo);
        try
        {
            var action = GenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, ref action, data) == 0;
        }
        finally
        {
            data.StateAction = 2; // WTD_STATEACTION_CLOSE
            var action = GenericVerifyV2;
            _ = WinVerifyTrust(IntPtr.Zero, ref action, data);
            fileInfo.Dispose();
            data.Dispose();
        }
    }

    public static bool SameTrustedPublisher(string client, string helper, out string reason)
    {
        if (!IsTrusted(client))
        {
            reason = "Client program is not validly signed.";
            return false;
        }

        try
        {
#pragma warning disable SYSLIB0057 // Signaturzertifikat einer PE-Datei; kein Ersatz in X509CertificateLoader fuer Authenticode.
            using var clientCertificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(client));
            using var helperCertificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(helper));
#pragma warning restore SYSLIB0057
            if (!string.Equals(clientCertificate.Thumbprint, helperCertificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Client and helper are not from the same publisher.";
                return false;
            }
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            reason = $"Signature not readable: {ex.Message}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, [In] WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo : IDisposable
    {
        public uint StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        public IntPtr FilePath;
        public IntPtr File;
        public IntPtr KnownSubject;

        public WinTrustFileInfo(string path) => FilePath = Marshal.StringToCoTaskMemUni(path);

        public void Dispose()
        {
            if (FilePath != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(FilePath);
                FilePath = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustData : IDisposable
    {
        public uint StructSize = (uint)Marshal.SizeOf<WinTrustData>();
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice = 2; // WTD_UI_NONE
        public uint RevocationChecks; // WTD_REVOKE_NONE: offline pruefbar
        public uint UnionChoice = 1; // WTD_CHOICE_FILE
        public IntPtr FileInfo;
        public uint StateAction = 1; // WTD_STATEACTION_VERIFY
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;

        public WinTrustData(WinTrustFileInfo fileInfo)
        {
            FileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, FileInfo, false);
        }

        public void Dispose()
        {
            if (FileInfo != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(FileInfo);
                FileInfo = IntPtr.Zero;
            }
        }
    }
}
