using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AscendedTweaks.Core;

namespace AscendedTweaks.Engine.State;

/// <summary>
/// Dateibasierter Zustandsspeicher: journal.json, ledger.json und Backup-Sets unter Backups\.
/// Alle Schreibvorgaenge laufen atomar ueber eine temporaere Datei.
/// </summary>
public sealed class StateStore
{
    private const string JournalFile = "journal.json";
    private const string LedgerFile = "ledger.json";
    private const string ChecksumFile = "checksums.json";
    private const string ManifestFile = "manifest.json";
    private const string BaselineFile = "baseline.json";

    public const string BaselineBatchId = "baseline";

    private readonly TimeProvider _time;

    public StateStore(string root, TimeProvider time)
    {
        Root = root;
        _time = time;
    }

    public string Root { get; }

    public string BackupsDirectory => Path.Combine(Root, "Backups");

    public static string DefaultUserRoot =>
        Path.Combine(ProductInfo.DataDirectory(Environment.SpecialFolder.LocalApplicationData), "State");

    public static string DefaultMachineRoot =>
        Path.Combine(ProductInfo.DataDirectory(Environment.SpecialFolder.CommonApplicationData), "State");

    /// <summary>Speicher fuer Benutzer-Richtlinien eines Benutzers (schreibt nur der Helfer, lesen darf jeder Benutzer).</summary>
    public static string UserPolicyRoot(string userSid)
    {
        ArgumentNullException.ThrowIfNull(userSid);
        if (!userSid.StartsWith("S-1-", StringComparison.Ordinal) || userSid.Skip(1).Any(c => !char.IsAsciiDigit(c) && c != '-'))
        {
            throw new AscendedTweaksException(ErrorKind.CatalogInvalid, "Invalid user SID.");
        }

        return Path.Combine(DefaultMachineRoot, "Users", userSid);
    }

    public JournalDocument LoadJournal() => Load<JournalDocument>(Path.Combine(Root, JournalFile)) ?? new JournalDocument();

    public void SaveJournal(JournalDocument journal) => Save(Path.Combine(Root, JournalFile), journal);

    public LedgerDocument LoadLedger() => Load<LedgerDocument>(Path.Combine(Root, LedgerFile)) ?? new LedgerDocument();

    public void SaveLedger(LedgerDocument ledger) => Save(Path.Combine(Root, LedgerFile), ledger);

    public string BackupSetPath(string backupSetId) => Path.Combine(BackupsDirectory, backupSetId);

    /// <summary>ID des Ausgangszustands (Abschnitt 7.3) oder null, wenn noch keiner erfasst ist.</summary>
    public string? LoadBaselineId() => Load<BaselineInfo>(Path.Combine(Root, BaselineFile))?.BackupSetId;

    public void SaveBaselineId(string backupSetId) => Save(Path.Combine(Root, BaselineFile), new BaselineInfo { BackupSetId = backupSetId });

    /// <summary>Alle Backup-Sets mit Manifest, neueste zuerst. Unlesbare Sets werden mit Readable = false gelistet.</summary>
    public IReadOnlyList<BackupSetInfo> ListBackupSets()
    {
        if (!Directory.Exists(BackupsDirectory))
        {
            return [];
        }

        var baseline = LoadBaselineId();
        var result = new List<BackupSetInfo>();
        foreach (var directory in Directory.EnumerateDirectories(BackupsDirectory))
        {
            var id = Path.GetFileName(directory);
            var size = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            BackupManifest? manifest = null;
            try
            {
                manifest = Load<BackupManifest>(Path.Combine(directory, ManifestFile));
            }
            catch (AscendedTweaksException)
            {
                // Beschaedigtes Manifest: Set bleibt sichtbar, damit es nicht unbemerkt verloren geht.
            }

            result.Add(new BackupSetInfo
            {
                Id = id,
                Path = directory,
                CreatedUtc = manifest?.CreatedUtc ?? Directory.GetCreationTimeUtc(directory),
                TweakIds = manifest?.TweakIds ?? [],
                IsBaseline = id == baseline,
                SizeBytes = size,
                Readable = manifest is not null,
            });
        }

        return result.OrderByDescending(b => b.CreatedUtc).ToList();
    }

    /// <summary>Loescht ein Backup-Set. Der Ausgangszustand und Sets angewendeter Tweaks sind geschuetzt.</summary>
    public void DeleteBackupSet(string backupSetId)
    {
        ArgumentNullException.ThrowIfNull(backupSetId);
        if (backupSetId.Length == 0 || backupSetId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
        {
            throw new AscendedTweaksException(ErrorKind.CatalogInvalid, "Invalid backup ID.");
        }

        if (backupSetId == LoadBaselineId())
        {
            throw new AscendedTweaksException(ErrorKind.General, "The original state is never deleted.");
        }

        if (LoadLedger().Entries.Any(e => e.BackupSetId == backupSetId)
            || LoadJournal().Batches.Any(b => b.BackupSetId == backupSetId && b.Status is BatchStatus.Pending or BatchStatus.Applying))
        {
            throw new AscendedTweaksException(ErrorKind.General, "This backup set belongs to tweaks that are still applied and cannot be deleted.");
        }

        Directory.Delete(BackupSetPath(backupSetId), recursive: true);
    }

    /// <summary>
    /// Exklusive Sperre fuer schreibende Vorgaenge, damit zwei Instanzen (App, CLI) nicht gleichzeitig
    /// Journal, Ledger und System veraendern. Die Sperre endet spaetestens mit dem Prozess.
    /// </summary>
    public IDisposable AcquireLock()
    {
        Directory.CreateDirectory(Root);
        try
        {
            return new FileStream(Path.Combine(Root, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException ex)
        {
            throw new AscendedTweaksException(ErrorKind.General, $"Another {ProductInfo.Name} instance is changing settings right now. Please wait and try again.", innerException: ex);
        }
    }

    /// <summary>Legt ein vollstaendiges Backup-Set an. Erst danach darf etwas veraendert werden (Regel S1b).</summary>
    public string CreateBackupSet(BackupManifest manifest, IReadOnlyList<BackupEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(entries);
        var id = string.Create(CultureInfo.InvariantCulture, $"{_time.GetUtcNow():yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
        var directory = BackupSetPath(id);
        Directory.CreateDirectory(Path.Combine(directory, "export"));

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ManifestFile] = Serialize(manifest),
        };

        foreach (var group in entries.GroupBy(e => CategoryFile(e.Capture)))
        {
            files[group.Key] = Serialize(group.ToList());
        }

        foreach (var tweak in entries.GroupBy(e => e.TweakId))
        {
            var reg = RegExport.Build(tweak.Select(e => e.Capture));
            if (reg is not null)
            {
                files[$"export/{ExportFileName(tweak.Key)}.reg"] = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(reg)).ToArray();
            }
        }

        foreach (var (name, content) in files)
        {
            WriteAtomic(Path.Combine(directory, name), content);
        }

        var checksums = files.ToDictionary(f => f.Key, f => Convert.ToHexString(SHA256.HashData(f.Value)), StringComparer.Ordinal);
        WriteAtomic(Path.Combine(directory, ChecksumFile), Serialize(checksums));
        return id;
    }

    /// <summary>Liest ein Backup-Set nach Pruefung aller Pruefsummen.</summary>
    public IReadOnlyList<BackupEntry> ReadBackupSet(string backupSetId)
    {
        var directory = BackupSetPath(backupSetId);
        var checksumPath = Path.Combine(directory, ChecksumFile);
        if (!File.Exists(checksumPath))
        {
            throw new AscendedTweaksException(ErrorKind.StateCorrupt, $"Backup set {backupSetId} has no checksums.");
        }

        var checksums = Deserialize<Dictionary<string, string>>(File.ReadAllBytes(checksumPath), checksumPath)!;
        var entries = new List<BackupEntry>();
        foreach (var (name, expected) in checksums)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                throw new AscendedTweaksException(ErrorKind.StateCorrupt, $"File {name} in backup set {backupSetId} is missing.");
            }

            var content = File.ReadAllBytes(path);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(content)), expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new AscendedTweaksException(ErrorKind.StateCorrupt, $"Checksum of {name} in backup set {backupSetId} does not match.");
            }

            if (name is "values.json" or "services.json" or "power.json" or "commands.json" or "tasks.json" or "parameters.json")
            {
                entries.AddRange(Deserialize<List<BackupEntry>>(content, path)!);
            }
        }

        return entries;
    }

    /// <summary>
    /// Dateiname fuer den .reg-Export. Katalog-IDs (MODUL-NNN) bleiben lesbar; erzeugte IDs (PERF-026/Ort/Name) enthalten
    /// beliebige Zeichen und bekommen einen bereinigten Namen mit Kurz-Hash, damit sie eindeutig bleiben.
    /// </summary>
    internal static string ExportFileName(string tweakId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(tweakId.Select(c => invalid.Contains(c) || c is '/' or '\\' or '.' ? '_' : c).ToArray());
        if (string.Equals(clean, tweakId, StringComparison.Ordinal) && clean.Length <= 40)
        {
            return clean;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tweakId)))[..8];
        return $"{clean[..Math.Min(clean.Length, 40)]}-{hash}";
    }

    private static string CategoryFile(ActionCapture capture)
    {
        return capture switch
        {
            RegistryValueCapture or RegistryKeyCapture or RegistryTreeCapture or RegistryListEntryCapture => "values.json",
            ServiceCapture => "services.json",
            PowerPlanCapture or PowerSettingCapture => "power.json",
            CommandCapture => "commands.json",
            ScheduledTaskCapture => "tasks.json",
            SystemParameterCapture => "parameters.json",
            _ => throw new AscendedTweaksException(ErrorKind.BackupFailed, $"Unknown backup type {capture.GetType().Name}."),
        };
    }

    private void Save<T>(string path, T document)
    {
        Directory.CreateDirectory(Root);
        WriteAtomic(path, Serialize(document));
    }

    private static T? Load<T>(string path)
        where T : class
    {
        return File.Exists(path) ? Deserialize<T>(File.ReadAllBytes(path), path) : null;
    }

    private static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, CoreJson.Options);

    private static T? Deserialize<T>(byte[] content, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(content, CoreJson.Options);
        }
        catch (JsonException ex)
        {
            // Nie still zuruecksetzen: ein beschaedigter Zustand wird gemeldet.
            throw new AscendedTweaksException(ErrorKind.StateCorrupt, $"State file {path} is damaged: {ex.Message}", innerException: ex);
        }
    }

    internal static void WriteAtomic(string path, byte[] content)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }
}
