using System.Text.RegularExpressions;

namespace AscendedTweaks.Core;

/// <summary>
/// Tweaks pro Spiel (Abschnitt 6.3), aus der ID erzeugt wie PERF-026:
/// GAME-007/&lt;EXE-Pfad&gt; (Vollbildoptimierung aus), GAME-008/&lt;1|2&gt;/&lt;EXE-Pfad&gt; (GPU-Praeferenz),
/// GAME-016/&lt;EXE-Name&gt; (dauerhafte CPU-Prioritaet, Experte).
/// </summary>
public static partial class GameTweaks
{
    public const string FullscreenOptimizations = "GAME-007";
    public const string GpuPreference = "GAME-008";
    public const string PermanentPriority = "GAME-016";

    public const string LayersPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    public const string GpuPreferencesPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string DisableFullscreenOptimizationsFlag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";
    private const string ImageOptions = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    private static readonly IReadOnlyList<WindowsEdition> AllEditions = [WindowsEdition.Home, WindowsEdition.Pro, WindowsEdition.Enterprise, WindowsEdition.Education];

    /// <summary>Absoluter Pfad zu einer .exe auf einem Laufwerk, ohne relative Teile und Steuerzeichen.</summary>
    public static bool IsValidExePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Length <= 260
            && ExePathPattern().IsMatch(path)
            && !path.Any(char.IsControl)
            && !path.Split('\\').Any(s => s is "." or "..");
    }

    public static bool IsValidExeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ExeNamePattern().IsMatch(name);
    }

    public static string FullscreenId(string exePath) => Checked(IsValidExePath(exePath), $"{FullscreenOptimizations}/{exePath}");

    /// <summary>1 = Energiesparen, 2 = Hochleistung.</summary>
    public static string GpuId(int preference, string exePath) =>
        Checked(IsValidExePath(exePath) && preference is 1 or 2, $"{GpuPreference}/{preference}/{exePath}");

    public static string PriorityId(string exeName) => Checked(IsValidExeName(exeName), $"{PermanentPriority}/{exeName}");

    public static bool IsGameId(string id) =>
        id.StartsWith(FullscreenOptimizations + "/", StringComparison.Ordinal)
        || id.StartsWith(GpuPreference + "/", StringComparison.Ordinal)
        || id.StartsWith(PermanentPriority + "/", StringComparison.Ordinal);

    /// <summary>Programmname fuer Anzeigen (Dateiname der EXE) oder null, wenn die ID nicht passt.</summary>
    public static string? TargetOf(string id) => TryCreate(id, out var tweak) ? TargetName(tweak) : null;

    public static bool TryCreate(string id, out TweakDefinition tweak)
    {
        ArgumentNullException.ThrowIfNull(id);
        tweak = null!;
        if (id.StartsWith(FullscreenOptimizations + "/", StringComparison.Ordinal))
        {
            var exe = id[(FullscreenOptimizations.Length + 1)..];
            if (!IsValidExePath(exe))
            {
                return false;
            }

            tweak = Create(id, FullscreenOptimizations, "Gaming.PerGame", RiskLevel.Caution, EvidenceLevel.Disputed, TweakScope.User, [new BuildRange { Min = 19045 }],
                new RegistryListEntryEqualsCondition { Hive = RegHive.HKCU, Path = LayersPath, Name = exe, Format = ListEntryFormat.CompatLayers, Key = DisableFullscreenOptimizationsFlag },
                new RegistryListEntryAction { Hive = RegHive.HKCU, Path = LayersPath, Name = exe, Format = ListEntryFormat.CompatLayers, Key = DisableFullscreenOptimizationsFlag },
                "Compatibility option 'Disable fullscreen optimizations' (EXE properties > Compatibility), stored as the flag DISABLEDXMAXIMIZEDWINDOWEDMODE under AppCompatFlags\\Layers. Not documented by Microsoft as a registry value; the effect differs per game, so without a measurement it is 'Disputed'. Other flags of the EXE are kept.");
            return true;
        }

        if (id.StartsWith(GpuPreference + "/", StringComparison.Ordinal))
        {
            var rest = id[(GpuPreference.Length + 1)..];
            if (rest.Length < 3 || rest[1] != '/' || rest[0] is not ('1' or '2') || !IsValidExePath(rest[2..]))
            {
                return false;
            }

            var exe = rest[2..];
            var value = rest[..1];
            tweak = Create(id, GpuPreference, "Gaming.PerGame", RiskLevel.Safe, EvidenceLevel.Disputed, TweakScope.User, [new BuildRange { Min = 19045 }],
                new RegistryListEntryEqualsCondition { Hive = RegHive.HKCU, Path = GpuPreferencesPath, Name = exe, Format = ListEntryFormat.KeyValueSemicolon, Key = "GpuPreference", Value = value },
                new RegistryListEntryAction { Hive = RegHive.HKCU, Path = GpuPreferencesPath, Name = exe, Format = ListEntryFormat.KeyValueSemicolon, Key = "GpuPreference", Value = value },
                "Same setting as Settings > System > Display > Graphics (GPU preference per app, 1 = power saving, 2 = high performance). The app changes only the GpuPreference entry and leaves other entries of the EXE alone. Only has an effect on PCs with more than one GPU (for example laptops with integrated and dedicated graphics). The registry value is not documented on Microsoft Learn (community sources only); applications can override the preference. Therefore 'Disputed'.");
            return true;
        }

        if (id.StartsWith(PermanentPriority + "/", StringComparison.Ordinal))
        {
            var exe = id[(PermanentPriority.Length + 1)..];
            if (!IsValidExeName(exe))
            {
                return false;
            }

            var path = $@"{ImageOptions}\{exe}\PerfOptions";
            tweak = Create(id, PermanentPriority, "Gaming.PerGame", RiskLevel.Expert, EvidenceLevel.Disputed, TweakScope.Machine, [new BuildRange { Min = 19045 }],
                new RegistryValueEqualsCondition { Hive = RegHive.HKLM, Path = path, Name = "CpuPriorityClass", Value = new RegLiteral { Number = 3 } },
                new RegistrySetValueAction { Hive = RegHive.HKLM, Path = path, Name = "CpuPriorityClass", Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 3 } },
                "Image File Execution Options\\<EXE>\\PerfOptions\\CpuPriorityClass = 3 (high). Not documented by Microsoft, no measured benefit. Can trigger antivirus heuristics; locked for games with anti-cheat.");
            return true;
        }

        return false;
    }

    private static string? TargetName(TweakDefinition tweak) => tweak.Apply[0] switch
    {
        RegistryListEntryAction a => Path.GetFileName(a.Name),
        RegistrySetValueAction a => a.Path.Split('\\')[^2],
        _ => null,
    };

    private static string Checked(bool valid, string id) =>
        valid ? id : throw new AscendedTweaksException(ErrorKind.CatalogInvalid, "Invalid program path or name.");

    private static TweakDefinition Create(
        string id,
        string baseId,
        string category,
        RiskLevel risk,
        EvidenceLevel evidence,
        TweakScope scope,
        IReadOnlyList<BuildRange> builds,
        DetectCondition detect,
        TweakAction action,
        string source)
    {
        return new TweakDefinition
        {
            Id = id,
            Category = category,
            Tags = ["fps"],
            NameKey = $"tweak.{baseId}.name",
            DescriptionKey = $"tweak.{baseId}.desc",
            SideEffectsKey = $"tweak.{baseId}.side",
            Risk = risk,
            Evidence = evidence,
            Builds = builds,
            Editions = AllEditions,
            Scope = scope,
            RequiresAdmin = scope != TweakScope.User,
            RequiresReboot = false,
            Detect = detect,
            Apply = [action],
            Revert = RevertSpec.RestoreFromBackup,
            Sources = [source],
            HiddenByDefault = evidence == EvidenceLevel.Disputed || risk == RiskLevel.Expert,
            Impact = evidence == EvidenceLevel.Disputed ? ImpactLevel.Unclear : ImpactLevel.Small,
            ImpactNoteKey = $"tweak.{baseId}.impact",
            PlainTitleKey = $"tweak.{baseId}.plain.title",
            PlainDescKey = $"tweak.{baseId}.plain.desc",
        };
    }

    [GeneratedRegex(@"^[A-Za-z]:\\([^\\/:*?""<>|]+\\)*[^\\/:*?""<>|]+\.exe$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExePathPattern();

    [GeneratedRegex(@"^[A-Za-z0-9 ._()\-]{1,100}\.exe$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExeNamePattern();
}
