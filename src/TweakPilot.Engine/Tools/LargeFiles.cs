using System.Diagnostics;
using TweakPilot.Engine.Cleanup;

namespace TweakPilot.Engine.Tools;

/// <summary>
/// Klassen grosser Dateien (V2 Abschnitt 6.2). Angezeigt werden nur A und B; C und D nur auf ausdruecklichen Klick und dann
/// nur mit "Im Explorer zeigen". Loeschen (in den Papierkorb) gibt es nur fuer A und B.
/// </summary>
public enum LargeFileClass
{
    /// <summary>"Kann meist weg".</summary>
    A,

    /// <summary>"Bitte pruefen".</summary>
    B,

    /// <summary>"Wahrscheinlich behalten".</summary>
    C,

    /// <summary>"Spiele und Systembereiche": nie auswaehlbar, nie loeschbar.</summary>
    D,
}

/// <summary>Eine gefundene Datei mit Klasse und Klartext "Warum sehe ich das?" (Textschluessel plus Werte).</summary>
public sealed record LargeFileItem(
    string Path,
    long SizeBytes,
    DateTimeOffset LastWriteUtc,
    LargeFileClass Class,
    string ReasonKey,
    IReadOnlyList<(string Name, object Value)> Args,
    bool IsCloud = false,
    string? DuplicateGroup = null,
    bool IsOriginal = false)
{
    /// <summary>Nur A und B, nie Cloud-Platzhalter.</summary>
    public bool Selectable => Class is LargeFileClass.A or LargeFileClass.B && !IsCloud;
}

/// <summary>Speicher pro Spiel: nur die Groesse des Spielordners; TweakPilot loescht keine Spielordner.</summary>
public sealed record GameFolder(string Path, string Name, long SizeBytes);

/// <param name="GameRoots">Erkannte Spielordner (Klasse D); der Loescher bekommt sie mit, damit nichts darunter je geloescht wird.</param>
public sealed record LargeFileScan(IReadOnlyList<LargeFileItem> Items, IReadOnlyList<GameFolder> Games, int Folders, bool Truncated, bool TimedOut, IReadOnlyList<string> GameRoots);

/// <summary>Bekannte Ordner des Benutzers und geschuetzte Wurzeln; null bedeutet "unbekannt".</summary>
public sealed record LargeFileContext
{
    public string? UserProfile { get; init; }

    public string? Downloads { get; init; }

    public string? Desktop { get; init; }

    public string? Documents { get; init; }

    public string? Pictures { get; init; }

    public string? Videos { get; init; }

    public string? Music { get; init; }

    public string? Temp { get; init; }

    /// <summary>Windows, Programme, Programme (x86), ProgramData: Klasse D.</summary>
    public IReadOnlyList<string> ProtectedRoots { get; init; } = [];

    /// <summary>Bekannte Cache-Ordner (Klasse A, wenn nicht kuerzlich geaendert).</summary>
    public IReadOnlyList<string> CacheFolders { get; init; } = [];

    /// <summary>Eintraege der "Immer behalten"-Liste (Dateien oder Ordner); sie werden nie angezeigt.</summary>
    public IReadOnlyList<string> Keep { get; init; } = [];

    public required DateTimeOffset Now { get; init; }
}

public interface IFileHasher
{
    /// <summary>SHA-256 als Hex; null, wenn die Datei nicht lesbar ist.</summary>
    string? Sha256(string path, CancellationToken cancellation);
}

public interface IFileInUseProbe
{
    bool IsInUse(string path);
}

/// <summary>Neue Logik fuer "grosse Dateien" (V2 Abschnitt 6): Klassifikation vor Anzeige, Spieluebersicht, Duplikate, KeepList.</summary>
public sealed class LargeFileScanner
{
    public const long DefaultThreshold = 500L * 1024 * 1024;
    public const int MaxEntries = 2_000_000;
    public const int MaxResults = 200;
    public static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(90);

    /// <summary>FILE_ATTRIBUTE_OFFLINE, FILE_ATTRIBUTE_RECALL_ON_OPEN und FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS.</summary>
    public const FileAttributes CloudAttributes = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;

    private static readonly HashSet<string> Installers = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".iso", ".zip", ".7z", ".rar" };
    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".rar", ".iso", ".tar", ".gz" };
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v" };
    internal static readonly HashSet<string> GameArchives = new(StringComparer.OrdinalIgnoreCase) { ".pak", ".utoc", ".ucas", ".bundle", ".vpk", ".wad", ".bsa", ".rpf", ".big", ".forge", ".arc", ".dat" };

    /// <summary>
    /// Spiel-typische Archive. Die allgemeinen Endungen .dat, .arc und .big kommen auch in Programmen und App-Daten vor; sie schuetzen
    /// (Klasse D), machen einen Ordner aber nicht zum Eintrag in "Speicher pro Spiel".
    /// </summary>
    private static readonly HashSet<string> StrongGameArchives = new(StringComparer.OrdinalIgnoreCase) { ".pak", ".utoc", ".ucas", ".bundle", ".vpk", ".wad", ".bsa", ".rpf", ".forge" };

    /// <summary>Ab dieser Groesse zaehlt ein spiel-typisches Archiv fuer "Speicher pro Spiel".</summary>
    private const long MinGameArchiveBytes = 100L * 1024 * 1024;

    /// <summary>Hoechstens so viele Ordner ueber der Archivdatei wird nach der ausfuehrbaren Datei gesucht.</summary>
    private const int MaxGameLevels = 4;

    /// <summary>Datenbanken, virtuelle Maschinen und Container, E-Mail-Archive, Passwort- und Wallet-Dateien: Klasse C.</summary>
    private static readonly HashSet<string> KeepExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".db", ".sqlite", ".sqlite3", ".mdf", ".ldf", ".accdb", ".mdb",
        ".vhd", ".vhdx", ".vmdk", ".vdi", ".qcow2", ".avhdx",
        ".pst", ".ost",
        ".kdbx", ".kdb", ".opvault", ".1pif", ".wallet",
    };

    private static readonly string[] LauncherSegments =
        ["steamapps", "XboxGames", "WindowsApps", "Epic Games", "Riot Games", "Battle.net", "Ubisoft Game Launcher", "EA Games", "GOG Games", "GOG Galaxy"];

    private static readonly string[] SystemSegments = ["System Volume Information", "$Recycle.Bin"];

    private static readonly HashSet<string> RootSystemFiles = new(StringComparer.OrdinalIgnoreCase) { "hiberfil.sys", "pagefile.sys", "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp" };

    private readonly ICleanupFileSystem _files;
    private readonly IFileHasher _hasher;
    private readonly LargeFileContext _context;
    private readonly IReadOnlyList<string> _skip;

    public LargeFileScanner(ICleanupFileSystem files, IFileHasher hasher, LargeFileContext context, IReadOnlyList<string> skipFolders)
    {
        _files = files;
        _hasher = hasher;
        _context = context;
        _skip = skipFolders;
    }

    public LargeFileScan Scan(IReadOnlyList<string> roots, long threshold, CancellationToken cancellation, IProgress<int>? folders = null, TimeSpan? limit = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var watch = Stopwatch.StartNew();
        var max = limit ?? TimeLimit;
        var large = new List<FsEntry>();
        var exeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gameArchiveDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var strongArchiveDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var markerRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var seen = 0;
        var count = 0;
        var truncated = false;
        var timedOut = false;
        var stack = new Stack<string>(roots.Where(_files.DirectoryExists));
        while (stack.Count > 0 && !truncated)
        {
            cancellation.ThrowIfCancellationRequested();
            if (watch.Elapsed > max)
            {
                timedOut = true;
                break;
            }

            var directory = stack.Pop();
            List<FsEntry> entries;
            try
            {
                entries = _files.List(directory).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            count++;
            folders?.Report(count);
            long bytes = 0;
            foreach (var entry in entries)
            {
                if (++seen > MaxEntries)
                {
                    truncated = true;
                    break;
                }

                var name = Path.GetFileName(entry.FullPath);
                if (entry.IsDirectory)
                {
                    if (name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    {
                        projectDirs.Add(directory);
                    }
                    else if (name.Equals(".egstore", StringComparison.OrdinalIgnoreCase))
                    {
                        markerRoots.Add(directory);
                    }

                    // Abzweigungen (Junctions, Symlinks) nie verfolgen; Windows-Ordner und "Immer behalten"-Ordner auslassen.
                    if (!entry.IsReparsePoint && !_skip.Any(s => SamePath(s, entry.FullPath)) && !IsKept(entry.FullPath))
                    {
                        stack.Push(entry.FullPath);
                    }

                    continue;
                }

                if (entry.IsReparsePoint)
                {
                    continue;
                }

                bytes += entry.Size;
                var extension = Path.GetExtension(name);
                if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    exeDirs.Add(directory);
                }
                else if (GameArchives.Contains(extension))
                {
                    gameArchiveDirs.Add(directory);
                    // Chromium- und Electron-Apps nutzen kleine .pak-Dateien (resources.pak); Spielarchive sind gross.
                    if (StrongGameArchives.Contains(extension) && entry.Size >= MinGameArchiveBytes)
                    {
                        strongArchiveDirs.Add(directory);
                    }
                }

                if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) || name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
                {
                    projectDirs.Add(directory);
                }

                if (name.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase) && extension.Equals(".acf", StringComparison.OrdinalIgnoreCase))
                {
                    // steamapps: jedes Spiel liegt unter steamapps\common\<Spiel>.
                    markerRoots.Add(Path.Combine(directory, "common"));
                }

                if (entry.Size >= threshold && !IsKept(entry.FullPath))
                {
                    large.Add(entry);
                }
            }

            dirBytes[directory] = bytes;
        }

        var structureRoots = StructureRoots(gameArchiveDirs, exeDirs);
        var gameRoots = TopOnly(structureRoots.Concat(markerRoots));
        var items = large.Select(e => Classify(e, gameRoots, projectDirs)).ToList();
        items = MarkDuplicates(items, cancellation);
        var visible = items.Where(i => i.Class is LargeFileClass.A or LargeFileClass.B).OrderByDescending(i => i.SizeBytes).Take(MaxResults);
        var hidden = items.Where(i => i.Class is LargeFileClass.C or LargeFileClass.D).OrderByDescending(i => i.SizeBytes).Take(MaxResults);
        var games = Games(markerRoots, StructureRoots(strongArchiveDirs, exeDirs), dirBytes);
        return new LargeFileScan(visible.Concat(hidden).ToList(), games, count, truncated, timedOut, gameRoots);
    }

    /// <summary>Kein Pfad, der zu Klasse D gehoert, darf geloescht werden; der Loescher fragt hier nach.</summary>
    public static bool IsProtectedPath(string path, IReadOnlyList<string> protectedRoots)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(protectedRoots);
        var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return protectedRoots.Any(r => IsUnder(path, r))
            || segments.Any(s => LauncherSegments.Contains(s, StringComparer.OrdinalIgnoreCase) || SystemSegments.Contains(s, StringComparer.OrdinalIgnoreCase))
            || (segments.Length == 2 && RootSystemFiles.Contains(segments[1]));
    }

    private bool IsKept(string path) => _context.Keep.Any(k => SamePath(k, path) || IsUnder(path, k));

    private LargeFileItem Classify(FsEntry entry, IReadOnlyList<string> gameRoots, HashSet<string> projectDirs)
    {
        var path = entry.FullPath;
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);
        var days = Math.Max(0, (int)(_context.Now - entry.LastWriteUtc).TotalDays);
        LargeFileItem Item(LargeFileClass c, string reason, params (string, object)[] args) =>
            new(path, entry.Size, entry.LastWriteUtc, c, reason, args, (entry.Attributes & CloudAttributes) != 0);

        // D zuerst: Spiele und Systembereiche schlagen jede andere Regel.
        if (gameRoots.FirstOrDefault(r => IsUnder(path, r)) is { } game)
        {
            return Item(LargeFileClass.D, "large.why.game", ("game", Path.GetFileName(game)));
        }

        if (IsProtectedPath(path, _context.ProtectedRoots))
        {
            return Item(LargeFileClass.D, "large.why.system");
        }

        if ((entry.Attributes & CloudAttributes) != 0)
        {
            return Item(LargeFileClass.C, "large.why.cloud");
        }

        if (days < 30)
        {
            return Item(LargeFileClass.C, "large.why.recent", ("days", days));
        }

        if (new[] { _context.Documents, _context.Pictures, _context.Videos, _context.Music }.Any(f => f is not null && IsUnder(path, f)))
        {
            return Item(LargeFileClass.C, "large.why.library");
        }

        if (projectDirs.Any(p => SamePath(p, Path.GetDirectoryName(path)!) || IsUnder(path, p)))
        {
            return Item(LargeFileClass.C, "large.why.project");
        }

        if (KeepExtensions.Contains(extension) || name.Equals("ext4.vhdx", StringComparison.OrdinalIgnoreCase) || name.Equals("wallet.dat", StringComparison.OrdinalIgnoreCase))
        {
            return Item(LargeFileClass.C, "large.why.keeptype");
        }

        var inDownloads = _context.Downloads is not null && IsUnder(path, _context.Downloads);
        if (inDownloads && Installers.Contains(extension))
        {
            return Item(LargeFileClass.A, "large.why.installer", ("days", days));
        }

        if (extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase))
        {
            return Item(LargeFileClass.A, "large.why.dump", ("days", days));
        }

        if (_context.CacheFolders.Any(c => IsUnder(path, c)))
        {
            return Item(LargeFileClass.A, "large.why.cache", ("days", days));
        }

        var inReview = inDownloads || new[] { _context.Desktop, _context.Temp }.Any(f => f is not null && IsUnder(path, f));
        if (inReview && days >= 90 && (Videos.Contains(extension) || Archives.Contains(extension)))
        {
            return Item(LargeFileClass.B, Videos.Contains(extension) ? "large.why.oldvideo" : "large.why.oldarchive", ("days", days));
        }

        return Item(LargeFileClass.C, "large.why.unknown");
    }

    /// <summary>
    /// Doppelte Dateien: gleiche Groesse, dann gleicher SHA-256. Die Originaldatei (Bibliothek, sonst die aelteste) behaelt ihre Klasse;
    /// eine Kopie ohne andere Einstufung wird zu B "Bitte pruefen". Cloud-Platzhalter und Klasse D werden nie gelesen.
    /// </summary>
    private List<LargeFileItem> MarkDuplicates(List<LargeFileItem> items, CancellationToken cancellation)
    {
        var candidates = items.Where(i => i.Class != LargeFileClass.D && !i.IsCloud).GroupBy(i => i.SizeBytes).Where(g => g.Count() > 1);
        var result = items.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var sameSize in candidates)
        {
            var byHash = sameSize
                .Select(i => (Item: i, Hash: _hasher.Sha256(i.Path, cancellation)))
                .Where(x => x.Hash is not null)
                .GroupBy(x => x.Hash!, StringComparer.Ordinal)
                .Where(g => g.Count() > 1);
            foreach (var group in byHash)
            {
                var members = group.Select(x => x.Item).ToList();
                var original = members.OrderBy(m => m.ReasonKey == "large.why.library" ? 0 : 1).ThenBy(m => m.LastWriteUtc).ThenBy(m => m.Path.Length).First();
                var key = group.Key[..12];
                foreach (var member in members)
                {
                    var isOriginal = ReferenceEquals(member, original);
                    var updated = member with { DuplicateGroup = key, IsOriginal = isOriginal };
                    if (!isOriginal && member.ReasonKey == "large.why.unknown")
                    {
                        updated = updated with { Class = LargeFileClass.B, ReasonKey = "large.why.duplicate", Args = [("original", Path.GetFileName(original.Path))] };
                    }

                    result[member.Path] = updated;
                }
            }
        }

        return items.Select(i => result[i.Path]).ToList();
    }

    /// <summary>
    /// Ordner mit Spiel-Binaerstruktur: der niedrigste Ordner ueber einer Spiel-Archivdatei (hoechstens vier Ebenen), unter dem auch
    /// eine ausfuehrbare Datei liegt. Nie ein Laufwerk oder ein Ordner direkt darunter (C:\Users, C:\Games), nie Systembereiche,
    /// nie das Benutzerprofil, seine direkten Unterordner oder die AppData-Wurzeln (Local, Roaming, LocalLow).
    /// </summary>
    private List<string> StructureRoots(HashSet<string> archiveDirs, HashSet<string> exeDirs)
    {
        var blocked = new List<string>();
        if (_context.UserProfile is { } profile)
        {
            blocked.Add(profile);
            blocked.Add(Path.Combine(profile, "AppData"));
            blocked.AddRange(new[] { "Local", "Roaming", "LocalLow" }.Select(r => Path.Combine(profile, "AppData", r)));
        }

        blocked.AddRange(new[] { _context.Downloads, _context.Desktop, _context.Documents, _context.Pictures, _context.Videos, _context.Music, _context.Temp }
            .Where(s => s is not null).Select(s => s!));
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in archiveDirs)
        {
            var current = dir;
            for (var level = 0; level < MaxGameLevels && current is not null; level++)
            {
                if (Depth(current) < 2 || blocked.Any(b => SamePath(b, current)) || (_context.UserProfile is { } p && SamePath(Path.GetDirectoryName(current) ?? string.Empty, p))
                    || current.Split('\\').Any(s => SystemSegments.Contains(s, StringComparer.OrdinalIgnoreCase)))
                {
                    break;
                }

                var candidate = current;
                if (exeDirs.Any(e => SamePath(e, candidate) || IsUnder(e, candidate)))
                {
                    roots.Add(candidate);
                    break;
                }

                current = Path.GetDirectoryName(current);
            }
        }

        return TopOnly(roots);
    }

    private static int Depth(string path) => path.TrimEnd('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries).Length - 1;

    private static List<string> TopOnly(IEnumerable<string> folders)
    {
        var all = folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return all.Where(r => !all.Any(o => !SamePath(o, r) && IsUnder(r, o))).ToList();
    }

    /// <summary>
    /// "Speicher pro Spiel": jedes Spiel unter steamapps\common, jeder .egstore-Ordner und jeder Ordner mit spiel-typischen Archiven
    /// (ohne .dat/.arc/.big). Ein Struktur-Ordner, der einen Launcher-Ordner enthaelt (zum Beispiel der ganze Steam-Ordner), zaehlt nicht.
    /// </summary>
    private static List<GameFolder> Games(HashSet<string> markers, List<string> strongRoots, Dictionary<string, long> dirBytes)
    {
        var folders = new List<string>();
        foreach (var marker in markers)
        {
            if (Path.GetFileName(marker).Equals("common", StringComparison.OrdinalIgnoreCase))
            {
                folders.AddRange(dirBytes.Keys.Where(d => SamePath(Path.GetDirectoryName(d) ?? string.Empty, marker)));
            }
            else
            {
                folders.Add(marker);
            }
        }

        folders.AddRange(strongRoots.Where(r => !markers.Any(m => IsUnder(m, r) || SamePath(m, r) || IsUnder(r, m))));
        return TopOnly(folders)
            .Select(f => new GameFolder(f, Path.GetFileName(f), dirBytes.Where(d => SamePath(d.Key, f) || IsUnder(d.Key, f)).Sum(d => d.Value)))
            .Where(g => g.SizeBytes > 0)
            .OrderByDescending(g => g.SizeBytes)
            .ToList();
    }

    public static bool IsUnder(string path, string folder) =>
        path.Length > folder.TrimEnd('\\').Length && path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string a, string b) => string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}

public sealed record RecycleOutcome(IReadOnlyList<string> Recycled, IReadOnlyList<(string Path, string ReasonKey)> Skipped);

/// <summary>
/// Loescht grosse Dateien nur in den Papierkorb (V2 Abschnitt 6.3). Direkt davor: Klasse, Schutzpfad, Existenz, Groesse,
/// Aenderungsdatum, Abzweigung, Cloud und "geoeffnet" erneut pruefen; bei einer Abweichung wird die Datei uebersprungen und gemeldet.
/// </summary>
public sealed class LargeFileRecycler
{
    private readonly ICleanupFileSystem _files;
    private readonly IRecycler _recycler;
    private readonly IFileInUseProbe _probe;
    private readonly IReadOnlyList<string> _protectedRoots;
    private readonly IReadOnlyList<string> _gameRoots;

    /// <param name="gameRoots">Spielordner aus dem Scan; zusaetzlich prueft der Loescher die Spielstruktur auf dem Datentraeger.</param>
    public LargeFileRecycler(ICleanupFileSystem files, IRecycler recycler, IFileInUseProbe probe, IReadOnlyList<string> protectedRoots, IReadOnlyList<string>? gameRoots = null)
    {
        _files = files;
        _recycler = recycler;
        _probe = probe;
        _protectedRoots = protectedRoots;
        _gameRoots = gameRoots ?? [];
    }

    public RecycleOutcome Recycle(IReadOnlyList<LargeFileItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var ready = new List<string>();
        var skipped = new List<(string, string)>();
        foreach (var item in items)
        {
            var reason = Check(item);
            if (reason is null)
            {
                ready.Add(item.Path);
            }
            else
            {
                skipped.Add((item.Path, reason));
            }
        }

        if (ready.Count > 0)
        {
            _recycler.Recycle(ready);
        }

        // Was danach noch da ist, wurde nicht verschoben (zum Beispiel vom Explorer abgelehnt).
        var recycled = new List<string>();
        foreach (var path in ready)
        {
            if (_files.Stat(path) is null)
            {
                recycled.Add(path);
            }
            else
            {
                skipped.Add((path, "large.skip.failed"));
            }
        }

        return new RecycleOutcome(recycled, skipped);
    }

    /// <summary>Spiel-Archivdatei mit einer ausfuehrbaren Datei in einem der acht Ordner darueber (ohne Laufwerkswurzel): nie loeschen.</summary>
    private bool InGameStructure(string path)
    {
        if (!LargeFileScanner.GameArchives.Contains(Path.GetExtension(path)))
        {
            return false;
        }

        var current = Path.GetDirectoryName(path);
        for (var level = 0; level < 8 && current is not null && Path.GetPathRoot(current) != current; level++)
        {
            if (ContainsExe(current, 3))
            {
                return true;
            }

            current = Path.GetDirectoryName(current);
        }

        return false;
    }

    private bool ContainsExe(string directory, int depth)
    {
        try
        {
            foreach (var entry in _files.List(directory))
            {
                if (!entry.IsDirectory && Path.GetExtension(entry.FullPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (entry.IsDirectory && !entry.IsReparsePoint && depth > 0 && ContainsExe(entry.FullPath, depth - 1))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nicht lesbar: im Zweifel schuetzen.
            return true;
        }

        return false;
    }

    private string? Check(LargeFileItem item)
    {
        if (!item.Selectable || PathGuard.Canonical(item.Path) is null || LargeFileScanner.IsProtectedPath(item.Path, _protectedRoots)
            || _gameRoots.Any(r => LargeFileScanner.IsUnder(item.Path, r)) || InGameStructure(item.Path))
        {
            return "large.skip.protected";
        }

        var now = _files.Stat(item.Path);
        if (now is null)
        {
            return "large.skip.missing";
        }

        if (now.IsReparsePoint || now.IsDirectory || (now.Attributes & LargeFileScanner.CloudAttributes) != 0)
        {
            return "large.skip.protected";
        }

        if (now.Size != item.SizeBytes || now.LastWriteUtc != item.LastWriteUtc)
        {
            return "large.skip.changed";
        }

        return _probe.IsInUse(item.Path) ? "large.skip.inuse" : null;
    }
}
