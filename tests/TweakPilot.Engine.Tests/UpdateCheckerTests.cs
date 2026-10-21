using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using TweakPilot.Engine.Insights;
using TweakPilot.Engine.Update;
using Xunit;

namespace TweakPilot.Engine.Tests;

public sealed class UpdateCheckerTests : IDisposable
{
    private static readonly Uri ManifestUri = new("https://updates.example.test/tweakpilot/manifest.json");
    private static readonly Uri InstallerUri = new("https://updates.example.test/tweakpilot/setup.exe");
    private const string Publisher = "Beispiel Herausgeber";
    private readonly FakeTransport _transport = new();
    private readonly FakeAuthenticode _authenticode = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TweakPilot.Tests", "update-" + Guid.NewGuid().ToString("N"));
    private readonly Ed25519PrivateKeyParameters _key;
    private readonly string _publicKey;
    private readonly byte[] _installer = Encoding.ASCII.GetBytes("MZ Beispiel-Installer");

    public UpdateCheckerTests()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        _key = (Ed25519PrivateKeyParameters)pair.Private;
        _publicKey = Convert.ToBase64String(((Ed25519PublicKeyParameters)pair.Public).GetEncoded());
        _transport.Responses[InstallerUri] = _installer;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Without_address_key_or_publisher_nothing_is_requested()
    {
        var checker = new UpdateChecker(_transport, _authenticode, UpdateConfig.ManifestUri, UpdateConfig.PublicKey, UpdateConfig.ExpectedPublisher, "0.1.0");

        (await checker.CheckAsync(CancellationToken.None)).Outcome.Should().Be(UpdateCheckOutcome.NotConfigured);
        _transport.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1.0.0", UpdateCheckOutcome.Available)]
    [InlineData("0.1.0", UpdateCheckOutcome.UpToDate)]
    [InlineData("0.0.9", UpdateCheckOutcome.UpToDate)]
    public async Task Signed_manifest_is_compared_with_the_current_version(string version, UpdateCheckOutcome outcome)
    {
        Publish(Manifest(version));

        var result = await Checker().CheckAsync(CancellationToken.None);

        result.Outcome.Should().Be(outcome);
        _transport.Requests.Should().OnlyContain(u => u.Scheme == "https");
    }

    [Fact]
    public async Task Tampered_manifest_or_foreign_key_is_rejected()
    {
        Publish(Manifest("1.0.0"));
        _transport.Responses[ManifestUri] = Encoding.UTF8.GetBytes(Manifest("9.0.0"));
        (await Checker().CheckAsync(CancellationToken.None)).Error.Should().Contain("signature");

        Publish(Manifest("1.0.0"));
        var other = Convert.ToBase64String(new byte[32]);
        (await new UpdateChecker(_transport, _authenticode, ManifestUri, other, Publisher, "0.1.0").CheckAsync(CancellationToken.None)).Outcome.Should().Be(UpdateCheckOutcome.Failed);
    }

    [Theory]
    [InlineData("""{"version":"1.0.0","installerUrl":"http://updates.example.test/setup.exe","sha256":"00","size":1}""")]
    [InlineData("""{"version":"1.0","installerUrl":"https://updates.example.test/setup.exe","sha256":"0000000000000000000000000000000000000000000000000000000000000000","size":1}""")]
    [InlineData("""{"version":"1.0.0","installerUrl":"https://updates.example.test/setup.exe","sha256":"0000000000000000000000000000000000000000000000000000000000000000","size":1,"run":"cmd"}""")]
    [InlineData("""{"version":"1.0.0","installerUrl":"https://updates.example.test/setup.exe","sha256":"0000000000000000000000000000000000000000000000000000000000000000","size":99999999999}""")]
    public async Task Signed_but_invalid_manifests_are_rejected(string manifest)
    {
        Publish(manifest);

        (await Checker().CheckAsync(CancellationToken.None)).Outcome.Should().Be(UpdateCheckOutcome.Failed);
    }

    [Fact]
    public async Task Download_checks_size_hash_and_publisher_and_deletes_bad_files()
    {
        Publish(Manifest("1.0.0"));
        var checker = Checker();
        var manifest = (await checker.CheckAsync(CancellationToken.None)).Manifest!;

        var ok = await checker.DownloadAsync(manifest, _directory, CancellationToken.None);
        ok.Ok.Should().BeTrue();
        File.ReadAllBytes(ok.InstallerPath!).Should().Equal(_installer);

        _authenticode.Result = new SignatureInfo(SignatureStatus.Valid, "Jemand anderes");
        var wrongPublisher = await checker.DownloadAsync(manifest, _directory, CancellationToken.None);
        wrongPublisher.Ok.Should().BeFalse();
        wrongPublisher.Error.Should().Contain("publisher");
        File.Exists(ok.InstallerPath!).Should().BeFalse("eine ungueltige Datei wird geloescht");

        _authenticode.Result = new SignatureInfo(SignatureStatus.Valid, Publisher);
        _transport.Responses[InstallerUri] = Encoding.ASCII.GetBytes("MZ Beispiel-Installex");
        (await checker.DownloadAsync(manifest, _directory, CancellationToken.None)).Error.Should().Contain("checksum");
    }

    [Theory]
    [InlineData("1.0.0-rc.1", "1.0.0", UpdateCheckOutcome.Available)]
    [InlineData("1.0.0-rc.1+abc", "0.9.9", UpdateCheckOutcome.UpToDate)]
    [InlineData("1.0.0", "1.0.0", UpdateCheckOutcome.UpToDate)]
    public async Task Release_candidate_is_older_than_the_final_version(string current, string offered, UpdateCheckOutcome outcome)
    {
        Publish(Manifest(offered));

        (await new UpdateChecker(_transport, _authenticode, ManifestUri, _publicKey, Publisher, current).CheckAsync(CancellationToken.None)).Outcome.Should().Be(outcome);
    }

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.2.3+abc", true)]
    [InlineData("1.2", false)]
    [InlineData("1.2.3-beta", false)]
    [InlineData("a.b.c", false)]
    public void Only_plain_semantic_versions_are_accepted(string text, bool valid)
    {
        (UpdateChecker.ParseVersion(text) is not null).Should().Be(valid);
    }

    private UpdateChecker Checker() => new(_transport, _authenticode, ManifestUri, _publicKey, Publisher, "0.1.0+local");

    private string Manifest(string version) =>
        $$"""{"version":"{{version}}","installerUrl":"{{InstallerUri}}","sha256":"{{Convert.ToHexString(SHA256.HashData(_installer))}}","size":{{_installer.Length}},"notes":"Test"}""";

    private void Publish(string manifest)
    {
        var bytes = Encoding.UTF8.GetBytes(manifest);
        var signer = new Ed25519Signer();
        signer.Init(true, _key);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        _transport.Responses[ManifestUri] = bytes;
        _transport.Responses[new Uri(ManifestUri + ".sig")] = Encoding.ASCII.GetBytes(Convert.ToBase64String(signer.GenerateSignature()));
    }

    private sealed class FakeTransport : IUpdateTransport
    {
        public Dictionary<Uri, byte[]> Responses { get; } = [];

        public List<Uri> Requests { get; } = [];

        public Task<byte[]> GetAsync(Uri uri, long maxBytes, CancellationToken cancellation)
        {
            Requests.Add(uri);
            var data = Responses.TryGetValue(uri, out var bytes) ? bytes : throw new HttpRequestException("404");
            return data.Length > maxBytes ? throw new InvalidDataException("zu gross") : Task.FromResult(data);
        }
    }

    private sealed class FakeAuthenticode : ISignatureVerifier
    {
        public SignatureInfo Result { get; set; } = new(SignatureStatus.Valid, Publisher);

        public SignatureInfo Verify(string filePath) => Result;
    }
}
