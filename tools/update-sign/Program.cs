using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace AscendedTweaks.Tools.UpdateSign;

/// <summary>
/// Signiert das Update-Manifest (Abschnitt 12.3) mit Ed25519.
///   update-sign keygen &lt;privater-schluessel-datei&gt;   erzeugt ein Schluesselpaar und gibt den oeffentlichen Schluessel aus
///   update-sign sign &lt;privater-schluessel-datei&gt; &lt;manifest.json&gt;   schreibt manifest.json.sig (Base64)
///   update-sign hash &lt;installer.exe&gt;   gibt SHA-256 und Groesse fuer das Manifest aus
/// Der private Schluessel darf nie im Repository liegen; das Werkzeug lehnt Pfade innerhalb eines Ordners mit .git oder AscendedTweaks.sln ab.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return args switch
            {
            ["keygen", var keyFile] => KeyGen(keyFile),
            ["sign", var keyFile, var manifest] => Sign(keyFile, manifest),
            ["hash", var installer] => Hash(installer),
                _ => Usage(),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Aufruf: update-sign keygen <schluesseldatei> | sign <schluesseldatei> <manifest.json> | hash <installer.exe>");
        return 5;
    }

    private static int KeyGen(string keyFile)
    {
        EnsureOutsideRepository(keyFile);
        if (File.Exists(keyFile))
        {
            throw new InvalidOperationException($"{keyFile} existiert bereits; ein vorhandener Schluessel wird nie ueberschrieben.");
        }

        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        var privateKey = ((Ed25519PrivateKeyParameters)pair.Private).GetEncoded();
        var publicKey = ((Ed25519PublicKeyParameters)pair.Public).GetEncoded();
        File.WriteAllText(keyFile, Convert.ToBase64String(privateKey));
        Console.WriteLine("Oeffentlicher Schluessel fuer UpdateConfig.PublicKey:");
        Console.WriteLine(Convert.ToBase64String(publicKey));
        return 0;
    }

    private static int Sign(string keyFile, string manifest)
    {
        EnsureOutsideRepository(keyFile);
        var key = new Ed25519PrivateKeyParameters(Convert.FromBase64String(File.ReadAllText(keyFile).Trim()), 0);
        var data = File.ReadAllBytes(manifest);
        var signer = new Ed25519Signer();
        signer.Init(true, key);
        signer.BlockUpdate(data, 0, data.Length);
        File.WriteAllText(manifest + ".sig", Convert.ToBase64String(signer.GenerateSignature()), Encoding.ASCII);
        Console.WriteLine($"Signatur geschrieben: {manifest}.sig");
        return 0;
    }

    private static int Hash(string installer)
    {
        var bytes = File.ReadAllBytes(installer);
        Console.WriteLine($"\"sha256\": \"{Convert.ToHexString(SHA256.HashData(bytes))}\", \"size\": {bytes.LongLength}");
        return 0;
    }

    private static void EnsureOutsideRepository(string keyFile)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(keyFile))!);
        for (var current = directory; current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, "AscendedTweaks.sln")))
            {
                throw new InvalidOperationException("Der private Schluessel darf nicht im Repository liegen. Waehle einen Ordner ausserhalb, zum Beispiel auf einem USB-Stick.");
            }
        }
    }
}
