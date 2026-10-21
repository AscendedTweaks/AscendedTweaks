using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using TweakPilot.Engine.Tools;

namespace TweakPilot.Engine.Windows;

/// <summary>SHA-256 fuer die Duplikatsuche; liest nur, Cloud-Platzhalter werden vom Scanner vorher ausgeschlossen.</summary>
[ExcludeFromCodeCoverage(Justification = "Thin file wrapper.")]
public sealed class WindowsFileHasher : IFileHasher
{
    public string? Sha256(string path, CancellationToken cancellation)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
            using var sha = SHA256.Create();
            var buffer = new byte[1 << 20];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                sha.TransformBlock(buffer, 0, read, null, 0);
            }

            sha.TransformFinalBlock([], 0, 0);
            return Convert.ToHexString(sha.Hash!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Geoeffnet = laesst sich nicht exklusiv oeffnen. Nur fuer lokale Dateien (Cloud-Platzhalter sind vorher ausgeschlossen).</summary>
[ExcludeFromCodeCoverage(Justification = "Thin file wrapper.")]
public sealed class WindowsFileInUseProbe : IFileInUseProbe
{
    public bool IsInUse(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
