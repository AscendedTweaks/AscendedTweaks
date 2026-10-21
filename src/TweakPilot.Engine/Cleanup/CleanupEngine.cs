using TweakPilot.Core;

namespace TweakPilot.Engine.Cleanup;

/// <summary>Eintrag im Dateisystem; Attributes enthaelt unter anderem die Cloud-Kennzeichen (Offline, RecallOnDataAccess).</summary>
public sealed record FsEntry(string FullPath, bool IsDirectory, bool IsReparsePoint, long Size, DateTimeOffset LastWriteUtc, FileAttributes Attributes = 0);

public enum DeleteOutcome
{
    Deleted,
    InUse,
    ReparsePoint,
    Missing,
    Failed,
}

/// <summary>Dateisystem fuer die Bereinigung. Implementierungen folgen nie Reparse Points.</summary>
public interface ICleanupFileSystem
{
    bool DirectoryExists(string path);

    /// <summary>Eintrag ohne Folgen von Reparse Points; null, wenn er fehlt.</summary>
    FsEntry? Stat(string path);

    /// <summary>Direkte Kinder eines Ordners. Wirft bei fehlendem Zugriff.</summary>
    IEnumerable<FsEntry> List(string directory);

    /// <summary>Loescht eine Datei ueber ein Handle, das Reparse Points nicht folgt (TOCTOU-arm, Abschnitt 9.4).</summary>
    DeleteOutcome DeleteFile(string path);

    /// <summary>Entfernt einen leeren Ordner, der kein Reparse Point ist.</summary>
    bool DeleteEmptyDirectory(string path);
}

public interface IRecycler
{
    /// <summary>Verschiebt die Dateien in den Papierkorb (Rueckgaengig ueber den Explorer moeglich).</summary>
    void Recycle(IReadOnlyList<string> paths);
}

public interface IProcessProbe
{
    bool IsRunning(string processName);
}

/// <summary>Sonderablaeufe (Abschnitt 6.5), getrennt testbar.</summary>
public interface ICleanupSpecials
{
    CleanupScan ScanRecycleBin();

    CleanupResult EmptyRecycleBin();

    /// <summary>Fuehrt den offiziellen Handler "Vorherige Windows-Installation(en)" aus.</summary>
    CleanupResult RunPreviousInstallations();

    CleanupResult RunComponentStoreCleanup();
}

/// <summary>
/// Bereinigung: immer erst scannen, dann loeschen (Abschnitt 6.5). Pfade kommen nur aus den eingebetteten Wurzeln;
/// Reparse Points werden nie verfolgt; geschuetzte Ordner werden nie beruehrt. Geloeschte Dateien sind nicht
/// ueber die Engine wiederherstellbar.
/// </summary>
public sealed class CleanupEngine
{
    private static readonly string[] UpdateServices = ["wuauserv", "bits"];
    private readonly ICleanupFileSystem _files;
    private readonly IRecycler _recycler;
    private readonly IProcessProbe _processes;
    private readonly ICleanupSpecials _specials;
    private readonly IServiceAccess _services;
    private readonly IReadOnlyDictionary<CleanupBase, string> _bases;
    private readonly IReadOnlyList<string> _protected;
    private readonly TimeProvider _time;
    private readonly TweakScope _scope;

    /// <param name="bases">Aufgeloeste Basisordner. Der Helfer bekommt nur Windows, ProgramData und Systemlaufwerk.</param>
    /// <param name="scope">Bereich dieses Prozesses: User im Benutzerprozess, Machine im Helfer.</param>
    public CleanupEngine(
        ICleanupFileSystem files,
        IRecycler recycler,
        IProcessProbe processes,
        ICleanupSpecials specials,
        IServiceAccess services,
        IReadOnlyDictionary<CleanupBase, string> bases,
        IReadOnlyList<string> protectedFolders,
        TweakScope scope,
        TimeProvider time)
    {
        _files = files;
        _recycler = recycler;
        _processes = processes;
        _specials = specials;
        _services = services;
        _bases = bases;
        _protected = protectedFolders;
        _scope = scope;
        _time = time;
    }

    public CleanupScan Scan(string categoryId)
    {
        var category = Require(categoryId);
        if (Blocked(category) is { } blocked)
        {
            return new CleanupScan { CategoryId = category.Id, Blocked = blocked };
        }

        return category.Handler switch
        {
            CleanupHandler.RecycleBin => _specials.ScanRecycleBin() with { CategoryId = category.Id },
            CleanupHandler.ComponentStore => new CleanupScan { CategoryId = category.Id, SizeUnknown = true },
            _ => Collect(category, out _),
        };
    }

    public CleanupResult Run(string categoryId, CleanupMode mode)
    {
        var category = Require(categoryId);
        if (mode == CleanupMode.RecycleBin && !category.SupportsRecycleBin && category.Handler == CleanupHandler.Files)
        {
            throw new TweakPilotException(ErrorKind.General, $"{category.Id} does not support the Recycle Bin; only permanent deletion is possible.");
        }

        if (Blocked(category) is { } blocked)
        {
            return new CleanupResult { CategoryId = category.Id, Mode = mode, Blocked = blocked };
        }

        return category.Handler switch
        {
            CleanupHandler.RecycleBin => _specials.EmptyRecycleBin() with { CategoryId = category.Id, Mode = CleanupMode.Permanent },
            CleanupHandler.PreviousInstallations => _specials.RunPreviousInstallations() with { CategoryId = category.Id, Mode = CleanupMode.Permanent },
            CleanupHandler.ComponentStore => _specials.RunComponentStoreCleanup() with { CategoryId = category.Id, Mode = CleanupMode.Permanent },
            CleanupHandler.WindowsUpdateDownload => WithUpdateServicesStopped(() => DeleteFiles(category, mode)),
            _ => DeleteFiles(category, mode),
        };
    }

    /// <summary>Loest eine Wurzel zu konkreten Ordnern (oder einer Datei) auf; "*" steht fuer jedes Unterverzeichnis ohne Reparse Point.</summary>
    internal IReadOnlyList<string> ResolveRoot(CleanupRoot root)
    {
        if (!_bases.TryGetValue(root.Base, out var basePath) || PathGuard.Canonical(basePath) is not { } current)
        {
            return [];
        }

        var candidates = new List<string> { current };
        foreach (var segment in root.RelativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = new List<string>();
            foreach (var parent in candidates)
            {
                if (segment.Contains('*', StringComparison.Ordinal))
                {
                    // "*" oder ein Muster wie "webcache_*": nur direkte Unterverzeichnisse mit passendem Namen.
                    if (_files.DirectoryExists(parent))
                    {
                        next.AddRange(SafeList(parent)
                            .Where(e => e.IsDirectory && !e.IsReparsePoint && Matches(Path.GetFileName(e.FullPath), segment))
                            .Select(e => e.FullPath));
                    }
                }
                else
                {
                    next.Add(Path.Combine(parent, segment));
                }
            }

            candidates = next;
        }

        return candidates
            .Select(PathGuard.Canonical)
            .OfType<string>()
            .Where(p => !PathGuard.IsProtected(p, _protected))
            .ToList();
    }

    private CleanupCategory Require(string categoryId)
    {
        if (!CleanupCategories.TryGet(categoryId, out var category))
        {
            throw new TweakPilotException(ErrorKind.CatalogInvalid, $"Unknown cleanup category {categoryId}.");
        }

        if (category.Scope != _scope)
        {
            throw new TweakPilotException(ErrorKind.PermissionDenied, $"{category.Id} belongs to scope {category.Scope}.");
        }

        return category;
    }

    private string? Blocked(CleanupCategory category)
    {
        var running = category.RequiresClosed.FirstOrDefault(_processes.IsRunning);
        return running is null ? null : $"browser-open:{running}";
    }

    private CleanupScan Collect(CleanupCategory category, out List<(string Root, FsEntry Entry)> files)
    {
        files = [];
        var skipped = 0;
        var cutoff = _time.GetUtcNow() - category.MinimumAge;
        foreach (var root in category.Roots)
        {
            foreach (var resolved in ResolveRoot(root))
            {
                if (root.SingleFile)
                {
                    if (_files.Stat(resolved) is { IsDirectory: false } single)
                    {
                        if (single.IsReparsePoint)
                        {
                            skipped++;
                        }
                        else if (single.LastWriteUtc <= cutoff)
                        {
                            files.Add((Path.GetDirectoryName(resolved)!, single));
                        }
                    }

                    continue;
                }

                if (!_files.DirectoryExists(resolved) || _files.Stat(resolved) is { IsReparsePoint: true })
                {
                    continue;
                }

                Walk(resolved, resolved, root, cutoff, files, ref skipped);
            }
        }

        return new CleanupScan
        {
            CategoryId = category.Id,
            Files = files.Count,
            Bytes = files.Sum(f => f.Entry.Size),
            SkippedReparsePoints = skipped,
        };
    }

    private void Walk(string root, string directory, CleanupRoot spec, DateTimeOffset cutoff, List<(string Root, FsEntry Entry)> files, ref int skipped)
    {
        foreach (var entry in SafeList(directory))
        {
            if (entry.IsReparsePoint)
            {
                skipped++;
                continue;
            }

            if (entry.IsDirectory)
            {
                if (spec.Recursive)
                {
                    Walk(root, entry.FullPath, spec, cutoff, files, ref skipped);
                }

                continue;
            }

            if (entry.LastWriteUtc <= cutoff && Matches(Path.GetFileName(entry.FullPath), spec.Pattern))
            {
                files.Add((root, entry));
            }
        }
    }

    private CleanupResult DeleteFiles(CleanupCategory category, CleanupMode mode)
    {
        Collect(category, out var files);
        int deleted = 0, inUse = 0, reparse = 0, failed = 0;
        long bytes = 0;

        // Vor jedem Loeschen erneut pruefen: kanonisch, unter der Wurzel, nicht geschuetzt (TOCTOU-Minimierung).
        var checkedFiles = new List<(string Root, FsEntry Entry)>();
        foreach (var (root, entry) in files)
        {
            if (PathGuard.Canonical(entry.FullPath) is { } path && PathGuard.IsStrictlyUnder(path, root) && !PathGuard.IsProtected(path, _protected))
            {
                checkedFiles.Add((root, entry));
            }
            else
            {
                failed++;
            }
        }

        if (mode == CleanupMode.RecycleBin)
        {
            var candidates = checkedFiles.Where(f => _files.Stat(f.Entry.FullPath) is { IsReparsePoint: false, IsDirectory: false }).ToList();
            reparse += checkedFiles.Count - candidates.Count;
            if (candidates.Count > 0)
            {
                _recycler.Recycle(candidates.Select(c => c.Entry.FullPath).ToList());
            }

            foreach (var (_, entry) in candidates)
            {
                if (_files.Stat(entry.FullPath) is null)
                {
                    deleted++;
                    bytes += entry.Size;
                }
                else
                {
                    inUse++;
                }
            }
        }
        else
        {
            foreach (var (_, entry) in checkedFiles)
            {
                switch (_files.DeleteFile(entry.FullPath))
                {
                    case DeleteOutcome.Deleted:
                        deleted++;
                        bytes += entry.Size;
                        break;
                    case DeleteOutcome.InUse:
                        inUse++;
                        break;
                    case DeleteOutcome.ReparsePoint:
                        reparse++;
                        break;
                    case DeleteOutcome.Missing:
                        break;
                    default:
                        failed++;
                        break;
                }
            }
        }

        if (category.RemoveEmptyFolders)
        {
            foreach (var root in category.Roots.Where(r => !r.SingleFile).SelectMany(ResolveRoot).Where(_files.DirectoryExists))
            {
                RemoveEmpty(root, root);
            }
        }

        return new CleanupResult
        {
            CategoryId = category.Id,
            Mode = mode,
            DeletedFiles = deleted,
            DeletedBytes = bytes,
            SkippedInUse = inUse,
            SkippedReparsePoints = reparse,
            Failed = failed,
        };
    }

    /// <summary>Entfernt leere Unterordner von unten nach oben; die Wurzel selbst bleibt.</summary>
    private void RemoveEmpty(string root, string directory)
    {
        foreach (var entry in SafeList(directory).Where(e => e.IsDirectory && !e.IsReparsePoint).ToList())
        {
            RemoveEmpty(root, entry.FullPath);
            if (PathGuard.IsStrictlyUnder(entry.FullPath, root) && !SafeList(entry.FullPath).Any())
            {
                _files.DeleteEmptyDirectory(entry.FullPath);
            }
        }
    }

    /// <summary>CLEAN-003: Dienste stoppen und danach exakt in den Ursprungszustand zuruecksetzen (Abschnitt 6.5).</summary>
    private CleanupResult WithUpdateServicesStopped(Func<CleanupResult> action)
    {
        var before = UpdateServices.ToDictionary(s => s, s => _services.GetConfig(s));
        try
        {
            foreach (var (name, config) in before)
            {
                if (config?.Running == true)
                {
                    _services.Stop(name, TimeSpan.FromSeconds(30));
                }
            }

            return action();
        }
        finally
        {
            foreach (var (name, config) in before.Reverse())
            {
                var now = _services.GetConfig(name);
                if (config is not null && now is not null && (now.StartType != config.StartType || now.DelayedAutoStart != config.DelayedAutoStart))
                {
                    _services.SetStartType(name, config.StartType, config.DelayedAutoStart);
                }

                if (config?.Running == true && _services.GetConfig(name)?.Running == false)
                {
                    _services.Start(name, TimeSpan.FromSeconds(30));
                }
            }
        }
    }

    private IEnumerable<FsEntry> SafeList(string directory)
    {
        try
        {
            return _files.List(directory).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    /// <summary>Einfaches Muster mit "*" (beliebig viele Zeichen), Gross-/Kleinschreibung egal.</summary>
    internal static bool Matches(string name, string pattern)
    {
        if (pattern == "*")
        {
            return true;
        }

        var parts = pattern.Split('*');
        if (!name.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var position = parts[0].Length;
        for (var i = 1; i < parts.Length; i++)
        {
            var last = i == parts.Length - 1;
            var index = last
                ? (name.Length - parts[i].Length >= position && name.EndsWith(parts[i], StringComparison.OrdinalIgnoreCase) ? name.Length - parts[i].Length : -1)
                : name.IndexOf(parts[i], position, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            position = index + parts[i].Length;
        }

        return parts.Length > 1 || position == name.Length;
    }
}
