using System.Globalization;
using System.Text.RegularExpressions;

namespace AscendedTweaks.Core;

/// <summary>
/// Erzeugte Update-Tweaks (Abschnitt 6.9): UPD-001 "UPD-001/&lt;Startdatum&gt;/&lt;Tage&gt;" (Pause, hoechstens 35 Tage)
/// und UPD-007 "UPD-007/&lt;10|11&gt;/&lt;Version&gt;" (Zielversion festhalten).
/// </summary>
public static partial class UpdateTweaks
{
    public const string PauseId = "UPD-001";
    public const string TargetId = "UPD-007";
    public const int MaxPauseDays = 35;

    private const string UxSettings = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
    private const string Policies = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

    private static readonly IReadOnlyList<WindowsEdition> AllEditions = [WindowsEdition.Home, WindowsEdition.Pro, WindowsEdition.Enterprise, WindowsEdition.Education];
    private static readonly IReadOnlyList<WindowsEdition> ProEditions = [WindowsEdition.Pro, WindowsEdition.Enterprise, WindowsEdition.Education];

    public static string PauseIdFor(DateOnly start, int days) => $"{PauseId}/{start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}/{days.ToString(CultureInfo.InvariantCulture)}";

    public static string TargetIdFor(int product, string release) => $"{TargetId}/{product.ToString(CultureInfo.InvariantCulture)}/{release}";

    /// <summary>Format der Pausenwerte, wie ihn die Einstellungen-App schreibt (ISO 8601, UTC).</summary>
    public static string PauseTime(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z";

    public static bool IsValidRelease(string release) => ReleasePattern().IsMatch(release);

    public static bool TryCreate(string id, out TweakDefinition tweak) => TryCreate(id, DateTimeOffset.UtcNow, out tweak);

    /// <summary>
    /// Ein Startdatum in der Zukunft wird abgelehnt: Sonst liesse sich die Pause ueber die 35 Tage hinaus verlaengern.
    /// Vergangene Daten bleiben gueltig, damit sich eine alte Pause noch zuruecksetzen laesst.
    /// </summary>
    public static bool TryCreate(string id, DateTimeOffset now, out TweakDefinition tweak)
    {
        ArgumentNullException.ThrowIfNull(id);
        tweak = null!;
        var parts = id.Split('/');
        if (parts.Length != 3)
        {
            return false;
        }

        if (parts[0] == PauseId)
        {
            if (!DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var days)
                || days is < 1 or > MaxPauseDays
                || PauseIdFor(start, days) != id
                || start.Year < 2020
                || start > DateOnly.FromDateTime(now.UtcDateTime).AddDays(1))
            {
                return false;
            }

            tweak = Pause(id, start, start.AddDays(days));
            return true;
        }

        if (parts[0] == TargetId)
        {
            if (parts[1] is not ("10" or "11") || !IsValidRelease(parts[2]))
            {
                return false;
            }

            tweak = Target(id, parts[1] == "11" ? "Windows 11" : "Windows 10", parts[2], parts[1] == "11");
            return true;
        }

        return false;
    }

    private static TweakDefinition Pause(string id, DateOnly start, DateOnly end)
    {
        var values = new (string Name, string Value)[]
        {
            ("PauseFeatureUpdatesStartTime", PauseTime(start)),
            ("PauseQualityUpdatesStartTime", PauseTime(start)),
            ("PauseFeatureUpdatesEndTime", PauseTime(end)),
            ("PauseQualityUpdatesEndTime", PauseTime(end)),
            ("PauseUpdatesExpiryTime", PauseTime(end)),
        };
        return new TweakDefinition
        {
            Id = id,
            Category = "Updates.Pause",
            Tags = ["updates"],
            Impact = ImpactLevel.Unclear,
            ImpactNoteKey = $"tweak.{PauseId}.impact",
            PlainTitleKey = $"tweak.{PauseId}.plain.title",
            PlainDescKey = $"tweak.{PauseId}.plain.desc",
            NameKey = $"tweak.{PauseId}.name",
            DescriptionKey = $"tweak.{PauseId}.desc",
            SideEffectsKey = $"tweak.{PauseId}.side",
            Risk = RiskLevel.Safe,

            // Die Werte unter UX\Settings schreibt die Einstellungen-App; dokumentiert sind nur die Richtlinien. Bis zur Sichtpruefung "Umstritten".
            Evidence = EvidenceLevel.Disputed,
            Builds = [new BuildRange { Min = 19045 }],
            Editions = AllEditions,
            Scope = TweakScope.Machine,
            RequiresAdmin = true,
            RequiresReboot = false,
            Detect = new AllOfCondition
            {
                Conditions = values.Select(v => (DetectCondition)new RegistryValueEqualsCondition { Hive = RegHive.HKLM, Path = UxSettings, Name = v.Name, Value = new RegLiteral { Text = v.Value } }).ToList(),
            },
            Apply = values.Select(v => (TweakAction)new RegistrySetValueAction { Hive = RegHive.HKLM, Path = UxSettings, Name = v.Name, Kind = RegValueKind.String, Value = new RegLiteral { Text = v.Value } }).ToList(),
            Revert = RevertSpec.RestoreFromBackup,
            Sources = ["Microsoft Learn: 'Configure Windows Update client policies', sections 'Pause feature updates' and 'Pause quality updates' (at most 35 days, then updates resume automatically). The values under UX\\Settings match the pause in Settings; they are not documented."],
            HiddenByDefault = true,
        };
    }

    private static TweakDefinition Target(string id, string product, string release, bool windows11)
    {
        return new TweakDefinition
        {
            Id = id,
            Category = "Updates.TargetVersion",
            Tags = ["updates"],
            Impact = ImpactLevel.Unclear,
            ImpactNoteKey = $"tweak.{TargetId}.impact",
            PlainTitleKey = $"tweak.{TargetId}.plain.title",
            PlainDescKey = $"tweak.{TargetId}.plain.desc",
            NameKey = $"tweak.{TargetId}.name",
            DescriptionKey = $"tweak.{TargetId}.desc",
            SideEffectsKey = $"tweak.{TargetId}.side",
            Risk = RiskLevel.Expert,

            // Richtlinie dokumentiert (Update Policy CSP), die Registry-Wertnamen stehen nur in der ADMX-Datei.
            Evidence = EvidenceLevel.Disputed,
            Builds = [windows11 ? new BuildRange { Min = 22000 } : new BuildRange { Min = 19045, Max = 19045 }],
            Editions = ProEditions,
            Scope = TweakScope.Machine,
            RequiresAdmin = true,
            RequiresReboot = false,
            Detect = new AllOfCondition
            {
                Conditions =
                [
                    new RegistryValueEqualsCondition { Hive = RegHive.HKLM, Path = Policies, Name = "TargetReleaseVersion", Value = new RegLiteral { Number = 1 } },
                    new RegistryValueEqualsCondition { Hive = RegHive.HKLM, Path = Policies, Name = "TargetReleaseVersionInfo", Value = new RegLiteral { Text = release } },
                    new RegistryValueEqualsCondition { Hive = RegHive.HKLM, Path = Policies, Name = "ProductVersion", Value = new RegLiteral { Text = product } },
                ],
            },
            Apply =
            [
                new RegistrySetValueAction { Hive = RegHive.HKLM, Path = Policies, Name = "TargetReleaseVersion", Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 1 } },
                new RegistrySetValueAction { Hive = RegHive.HKLM, Path = Policies, Name = "TargetReleaseVersionInfo", Kind = RegValueKind.String, Value = new RegLiteral { Text = release } },
                new RegistrySetValueAction { Hive = RegHive.HKLM, Path = Policies, Name = "ProductVersion", Kind = RegValueKind.String, Value = new RegLiteral { Text = product } },
            ],
            Revert = RevertSpec.RestoreFromBackup,
            Sources = ["Microsoft Learn: Update Policy CSP, 'TargetReleaseVersion' and 'ProductVersion' (group policy 'Select the target Feature Update version', WindowsUpdate.admx). The value names TargetReleaseVersion, TargetReleaseVersionInfo and ProductVersion come from the ADMX file."],
            HiddenByDefault = true,
        };
    }

    [GeneratedRegex(@"^[0-9]{2}H[12]$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleasePattern();
}
