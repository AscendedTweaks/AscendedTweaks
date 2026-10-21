using System.Diagnostics.CodeAnalysis;
using TweakPilot.Engine.Tools;

namespace TweakPilot.Engine.Windows;

/// <summary>Benutzerordner, geschuetzte Wurzeln und Cache-Ordner fuer die Suche nach grossen Dateien (App und Kommandozeile).</summary>
[ExcludeFromCodeCoverage(Justification = "Reads environment values only.")]
public static class WindowsLargeFileContext
{
    public static IReadOnlyList<string> ProtectedRoots() =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        }.Where(p => !string.IsNullOrEmpty(p)).ToList();

    public static LargeFileContext Create(IReadOnlyList<string> keep)
    {
        static string? Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder) is { Length: > 0 } p ? p : null;
        var local = Folder(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Folder(Environment.SpecialFolder.ApplicationData);
        string[] caches = local is null
            ? []
            : [
                Path.Combine(local, "D3DSCache"), Path.Combine(local, @"NVIDIA\DXCache"), Path.Combine(local, @"NVIDIA\GLCache"), Path.Combine(local, @"AMD\DxCache"),
                Path.Combine(local, @"Steam\htmlcache"), Path.Combine(local, @"Spotify\Storage"), Path.Combine(roaming ?? local, @"discord\Cache"),
            ];
        return new LargeFileContext
        {
            UserProfile = Folder(Environment.SpecialFolder.UserProfile),
            Downloads = KnownFolders.Downloads(),
            Desktop = Folder(Environment.SpecialFolder.DesktopDirectory),
            Documents = Folder(Environment.SpecialFolder.MyDocuments),
            Pictures = Folder(Environment.SpecialFolder.MyPictures),
            Videos = Folder(Environment.SpecialFolder.MyVideos),
            Music = Folder(Environment.SpecialFolder.MyMusic),
            Temp = Path.GetTempPath().TrimEnd('\\'),
            ProtectedRoots = ProtectedRoots(),
            CacheFolders = caches,
            Keep = keep,
            Now = DateTimeOffset.UtcNow,
        };
    }
}
