using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AscendedTweaks.Engine.Insights;

public enum GameSource
{
    Epic,
    Steam,
    Manual,
}

[Flags]
public enum AntiCheat
{
    None = 0,
    EasyAntiCheat = 1,
    BattlEye = 2,
    Vanguard = 4,
}

/// <summary>Ein gefundenes Spiel. <see cref="ExePath"/> fehlt, wenn der Launcher keine EXE nennt (Steam): dann waehlt der Nutzer sie aus.</summary>
public sealed record DetectedGame(string Name, GameSource Source, string InstallFolder, string? ExePath, string? LaunchUri, AntiCheat AntiCheat);

public interface IGameFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    IEnumerable<string> EnumerateFiles(string folder, string pattern);

    IEnumerable<string> EnumerateDirectories(string folder);

    string ReadAllText(string path);
}

/// <summary>
/// Launcher-Erkennung (Abschnitt 6.3), nur lesend: Epic-Manifeste unter %ProgramData% und Steam-Bibliotheken aus
/// libraryfolders.vdf. Es wird nichts gestartet und nichts geschrieben.
/// </summary>
public sealed partial class GameLibrary
{
    /// <summary>
    /// Spiele, deren Launcher eine Start-EXE nennt, die nicht der eigentliche Spielprozess ist.
    /// ANNAHME: Fortnite startet ueber FortniteBootstrapper.exe; GPU- und Vollbild-Einstellungen gehoeren an den Spielprozess.
    /// </summary>
    private static readonly Dictionary<string, string> KnownGameExe = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Fortnite"] = @"FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe",
    };

    private readonly IGameFileSystem _files;
    private readonly string? _epicManifests;
    private readonly string? _steamRoot;

    public GameLibrary(IGameFileSystem files, string? epicManifests, string? steamRoot)
    {
        _files = files;
        _epicManifests = epicManifests;
        _steamRoot = steamRoot;
    }

    public sealed record EpicManifest(string DisplayName, string AppName, string InstallLocation, string LaunchExecutable, string MainGameAppName);

    public static EpicManifest? ParseEpicManifest(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string Get(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
            var manifest = new EpicManifest(Get("DisplayName"), Get("AppName"), Get("InstallLocation"), Get("LaunchExecutable"), Get("MainGameAppName"));
            return manifest.AppName.Length == 0 || manifest.InstallLocation.Length == 0 ? null : manifest;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Alle "path"-Eintraege aus libraryfolders.vdf (Backslashes sind dort verdoppelt).</summary>
    public static IReadOnlyList<string> ParseLibraryFolders(string vdf)
    {
        ArgumentNullException.ThrowIfNull(vdf);
        return PathPattern().Matches(vdf).Select(m => m.Groups["v"].Value.Replace(@"\\", @"\", StringComparison.Ordinal)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static (string Name, string InstallDir)? ParseAppManifest(string acf)
    {
        ArgumentNullException.ThrowIfNull(acf);
        var name = NamePattern().Match(acf);
        var dir = InstallDirPattern().Match(acf);
        return name.Success && dir.Success ? (name.Groups["v"].Value, dir.Groups["v"].Value) : null;
    }

    /// <summary>Sucht bekannte Anti-Cheat-Ordner und -Dateien bis zu drei Ebenen tief (begrenzt, nur lesend).</summary>
    public AntiCheat DetectAntiCheat(string folder)
    {
        var result = AntiCheat.None;
        if (folder.Contains(@"\Riot Games\", StringComparison.OrdinalIgnoreCase))
        {
            result |= AntiCheat.Vanguard;
        }

        var budget = 2000;
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((folder, 0));
        while (queue.Count > 0 && budget > 0)
        {
            var (path, depth) = queue.Dequeue();
            IEnumerable<string> directories;
            try
            {
                directories = _files.EnumerateDirectories(path).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                budget--;
                var name = Path.GetFileName(directory);
                if (name.StartsWith("EasyAntiCheat", StringComparison.OrdinalIgnoreCase))
                {
                    result |= AntiCheat.EasyAntiCheat;
                }
                else if (name.Equals("BattlEye", StringComparison.OrdinalIgnoreCase))
                {
                    result |= AntiCheat.BattlEye;
                }
                else if (depth < 3)
                {
                    queue.Enqueue((directory, depth + 1));
                }
            }
        }

        return result;
    }

    public IReadOnlyList<DetectedGame> Scan()
    {
        var games = new List<DetectedGame>();
        ScanEpic(games);
        ScanSteam(games);
        return games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void ScanEpic(List<DetectedGame> games)
    {
        if (_epicManifests is null || !_files.DirectoryExists(_epicManifests))
        {
            return;
        }

        foreach (var file in _files.EnumerateFiles(_epicManifests, "*.item"))
        {
            var manifest = ParseEpicManifest(SafeRead(file));

            // Spielmodi und Editoren (MainGameAppName gesetzt) gehoeren zum Hauptspiel und werden nicht doppelt gelistet.
            if (manifest is null || manifest.MainGameAppName.Length > 0 || !_files.DirectoryExists(manifest.InstallLocation))
            {
                continue;
            }

            string? exe = null;
            if (KnownGameExe.TryGetValue(manifest.AppName, out var known))
            {
                exe = Path.Combine(manifest.InstallLocation, known);
            }
            else if (manifest.LaunchExecutable.Length > 0)
            {
                exe = Path.Combine(manifest.InstallLocation, manifest.LaunchExecutable.Replace('/', '\\'));
            }

            games.Add(new DetectedGame(
                manifest.DisplayName.Length > 0 ? manifest.DisplayName : manifest.AppName,
                GameSource.Epic,
                manifest.InstallLocation,
                exe is not null && _files.FileExists(exe) ? exe : null,
                $"com.epicgames.launcher://apps/{Uri.EscapeDataString(manifest.AppName)}?action=launch&silent=true",
                DetectAntiCheat(manifest.InstallLocation)));
        }
    }

    private void ScanSteam(List<DetectedGame> games)
    {
        if (_steamRoot is null)
        {
            return;
        }

        var vdf = Path.Combine(_steamRoot, "steamapps", "libraryfolders.vdf");
        var libraries = _files.FileExists(vdf) ? ParseLibraryFolders(SafeRead(vdf)) : [_steamRoot];
        foreach (var library in libraries)
        {
            var apps = Path.Combine(library, "steamapps");
            if (!_files.DirectoryExists(apps))
            {
                continue;
            }

            foreach (var file in _files.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                if (ParseAppManifest(SafeRead(file)) is not { } app)
                {
                    continue;
                }

                // Steamworks-Pakete und Laufzeitumgebungen sind keine Spiele.
                if (app.Name.StartsWith("Steamworks", StringComparison.OrdinalIgnoreCase) || app.Name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var folder = Path.Combine(apps, "common", app.InstallDir);
                if (!_files.DirectoryExists(folder))
                {
                    continue;
                }

                var id = AppIdPattern().Match(Path.GetFileName(file)).Groups["id"].Value;
                games.Add(new DetectedGame(app.Name, GameSource.Steam, folder, null, id.Length > 0 ? $"steam://rungameid/{id}" : null, DetectAntiCheat(folder)));
            }
        }
    }

    private string SafeRead(string path)
    {
        try
        {
            return _files.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    [GeneratedRegex(@"""path""\s+""(?<v>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"""name""\s+""(?<v>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"""installdir""\s+""(?<v>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex InstallDirPattern();

    [GeneratedRegex(@"^appmanifest_(?<id>[0-9]+)\.acf$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AppIdPattern();
}

[ExcludeFromCodeCoverage(Justification = "Thin wrapper around the file system.")]
public sealed class WindowsGameFileSystem : IGameFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> EnumerateFiles(string folder, string pattern) => Directory.EnumerateFiles(folder, pattern);

    public IEnumerable<string> EnumerateDirectories(string folder) =>
        new DirectoryInfo(folder).EnumerateDirectories()
            .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
            .Select(d => d.FullName);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public static string EpicManifestFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public static string? SteamRoot()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") is string path ? Path.GetFullPath(path.Replace('/', '\\')) : null;
    }
}
