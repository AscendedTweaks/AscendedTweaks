using AscendedTweaks.Core;

namespace AscendedTweaks.Catalog;

/// <summary>
/// Inhaltliche Regeln, die das Schema nicht ausdruecken kann (Regel S4).
/// Ein Tweak mit Befund wird nicht ausgeliefert, der Build schlaegt ueber Tests und tools/catalog-lint fehl.
/// </summary>
public static class CatalogLinter
{
    public static readonly IReadOnlyList<string> RequiredCultures = ["de-DE", "en-US"];

    private static readonly Dictionary<string, string> ModuleCategory = new(StringComparer.Ordinal)
    {
        ["PRIV"] = "Privacy",
        ["PERF"] = "Performance",
        ["GAME"] = "Gaming",
        ["CLEAN"] = "Cleanup",
        ["DEB"] = "Debloat",
        ["UI"] = "Ui",
        ["NET"] = "Network",
        ["DISK"] = "Disk",
        ["UPD"] = "Updates",
        ["BROW"] = "Performance",
        ["POWR"] = "Performance",
        ["DEFN"] = "Performance",
    };

    public static IReadOnlyList<LintIssue> Lint(IReadOnlyList<TweakDefinition> tweaks, IReadOnlyDictionary<string, TextBundle> texts)
    {
        ArgumentNullException.ThrowIfNull(tweaks);
        ArgumentNullException.ThrowIfNull(texts);
        var issues = new List<LintIssue>();

        foreach (var culture in RequiredCultures.Where(c => !texts.ContainsKey(c)))
        {
            issues.Add(new LintIssue(null, "i18n", $"Language file {culture} is missing."));
        }

        foreach (var duplicate in tweaks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            issues.Add(new LintIssue(duplicate.Key, "unique-id", "ID is used more than once."));
        }

        foreach (var tweak in tweaks)
        {
            LintTweak(tweak, texts, issues);
        }

        return issues;
    }

    private static void LintTweak(TweakDefinition t, IReadOnlyDictionary<string, TextBundle> texts, List<LintIssue> issues)
    {
        void Add(string rule, string message) => issues.Add(new LintIssue(t.Id, rule, message));

        if (!ModuleCategory.TryGetValue(t.Module, out var category) || !t.Category.StartsWith(category + ".", StringComparison.Ordinal))
        {
            Add("category", $"Category {t.Category} does not match module {t.Module}.");
        }

        LintTexts(t, texts, Add);

        if (t.Tags.Count is < 1 or > 3 || t.Tags.Distinct(StringComparer.Ordinal).Count() != t.Tags.Count)
        {
            Add("tags", "One to three different tags are required.");
        }

        foreach (var tag in t.Tags.Where(tag => !TweakTags.All.Contains(tag)))
        {
            Add("tags", $"Unknown tag {tag}; allowed are {string.Join(", ", TweakTags.All)}.");
        }

        if (t.Sources.Count == 0 || t.Sources.Any(string.IsNullOrWhiteSpace))
        {
            Add("sources", "At least one non-empty source is required.");
        }

        foreach (var range in t.Builds.Where(b => b.Max is { } max && max < b.Min))
        {
            Add("builds", $"Build range {range.Min} to {range.Max} is empty.");
        }

        LintImpact(t, Add);

        if (t.Evidence == EvidenceLevel.Disputed && !t.HiddenByDefault)
        {
            Add("hidden", "Tweaks with evidence 'Disputed' must be hidden by default.");
        }

        if (t.Scope is TweakScope.Machine or TweakScope.UserPolicy && !t.RequiresAdmin)
        {
            Add("admin", "System-wide tweaks and user policies need administrator rights.");
        }

        if (t.Scope == TweakScope.User && t.RequiresAdmin)
        {
            Add("admin", "User tweaks run without the helper and must not require administrator rights.");
        }

        var allActions = t.Apply.Concat(t.Revert.Actions ?? []).ToList();
        foreach (var action in allActions)
        {
            LintAction(t, action, Add);
        }

        foreach (var leaf in DetectTypeNames.LeavesOf(t.Detect))
        {
            LintConditionPaths(leaf, Add);
        }

        LintApplyMatchesDetect(t, Add);
    }

    public const int MaxPlainTitleWords = 5;
    public const int MaxPlainDescLength = 90;

    /// <summary>V2, Abschnitt 5.1 und 8: Wirkung ist Pflicht, "Spuerbar" nur mit Messbeleg, "Unklar" nur versteckt.</summary>
    private static void LintImpact(TweakDefinition t, Action<string, string> add)
    {
        if (t.Impact is not { } impact)
        {
            add("impact", "'impact' is missing (Noticeable, Small or Unclear).");
            return;
        }

        if (impact == ImpactLevel.Noticeable && (t.MeasuredOn.Count == 0 || t.MeasuredOn.Any(string.IsNullOrWhiteSpace)))
        {
            add("impact", "'Noticeable' needs at least one measurement in 'measuredOn'.");
        }

        if (t.Evidence == EvidenceLevel.Disputed && impact != ImpactLevel.Unclear)
        {
            add("impact", "Tweaks with evidence 'Disputed' have the impact 'Unclear'.");
        }

        if (impact == ImpactLevel.Unclear && !t.HiddenByDefault)
        {
            add("impact", "Tweaks with impact 'Unclear' belong in expert mode (hiddenByDefault).");
        }
    }

    /// <summary>Klartexte fuer den Einfach-Modus: Titel hoechstens 5 Woerter, Satz hoechstens 90 Zeichen, in jeder Sprache.</summary>
    public static IEnumerable<string> PlainTextProblems(string? title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            yield return "Title is missing";
        }
        else if (title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxPlainTitleWords)
        {
            yield return $"Title has more than {MaxPlainTitleWords} words";
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            yield return "Sentence is missing";
        }
        else if (description.Length > MaxPlainDescLength)
        {
            yield return $"Sentence is longer than {MaxPlainDescLength} characters ({description.Length})";
        }
    }

    private static void LintTexts(TweakDefinition t, IReadOnlyDictionary<string, TextBundle> texts, Action<string, string> add)
    {
        var expected = new[]
        {
            (t.NameKey, $"tweak.{t.Id}.name"),
            (t.DescriptionKey, $"tweak.{t.Id}.desc"),
            (t.SideEffectsKey, $"tweak.{t.Id}.side"),
            (t.ImpactNoteKey ?? string.Empty, $"tweak.{t.Id}.impact"),
            (t.PlainTitleKey ?? string.Empty, $"tweak.{t.Id}.plain.title"),
            (t.PlainDescKey ?? string.Empty, $"tweak.{t.Id}.plain.desc"),
        };
        foreach (var culture in RequiredCultures)
        {
            if (texts.TryGetValue(culture, out var plain))
            {
                plain.Texts.TryGetValue(t.PlainTitleKey ?? string.Empty, out var title);
                plain.Texts.TryGetValue(t.PlainDescKey ?? string.Empty, out var description);
                foreach (var problem in PlainTextProblems(title, description))
                {
                    add("plain-text", $"Klartext {culture}: {problem}.");
                }
            }
        }

        foreach (var (actual, wanted) in expected)
        {
            if (!string.Equals(actual, wanted, StringComparison.Ordinal))
            {
                add("text-key", $"Key {actual} must be named {wanted}.");
            }

            foreach (var culture in RequiredCultures)
            {
                if (!texts.TryGetValue(culture, out var bundle)
                    || !bundle.Texts.TryGetValue(actual, out var text)
                    || string.IsNullOrWhiteSpace(text))
                {
                    add("text-missing", $"Text {actual} is missing in {culture}.");
                }
            }
        }
    }

    private static void LintAction(TweakDefinition t, TweakAction action, Action<string, string> add)
    {
        var expectedScope = action switch
        {
            RegistrySetValueAction a => RegistryPaths.ScopeFor(a.Hive, a.Path),
            RegistryDeleteValueAction a => RegistryPaths.ScopeFor(a.Hive, a.Path),
            RegistryCreateKeyAction a => RegistryPaths.ScopeFor(a.Hive, a.Path),
            RegistryDeleteKeyAction a => RegistryPaths.ScopeFor(a.Hive, a.Path),
            RegistryListEntryAction a => RegistryPaths.ScopeFor(a.Hive, a.Path),
            SystemParametersInfoAction => TweakScope.User,
            DisplayRefreshRateAction => TweakScope.User,
            _ => TweakScope.Machine,
        };
        if (expectedScope != t.Scope)
        {
            add("scope", $"Action {ActionTypeNames.NameOf(action)} requires scope {expectedScope}; the tweak has {t.Scope}.");
        }

        switch (action)
        {
            case RegistrySetValueAction a:
                CheckPath(a.Hive, a.Path, add);
                if (!a.Value.TryToData(a.Kind, out _, out var error))
                {
                    add("value", error!);
                }

                break;
            case RegistryDeleteValueAction a:
                CheckPath(a.Hive, a.Path, add);
                break;
            case RegistryCreateKeyAction a:
                CheckPath(a.Hive, a.Path, add);
                CheckKeyAllowlist(a.Hive, a.Path, add);
                break;
            case RegistryDeleteKeyAction a:
                CheckPath(a.Hive, a.Path, add);
                CheckKeyAllowlist(a.Hive, a.Path, add);
                break;
            case CommandAction a:
                if (!CommandWhitelist.All.TryGetValue(a.Command, out var definition))
                {
                    add("command", $"Command {a.Command} is not on the allowlist.");
                }
                else if (!definition.IsAllowed(a.Value))
                {
                    add("command", $"Value {a.Value} is not allowed for {a.Command}.");
                }

                break;
            case ScheduledTaskSetEnabledAction a:
                CheckTaskPath(a.TaskPath, add);
                break;
            case RegistryListEntryAction a:
                CheckPath(a.Hive, a.Path, add);
                if (a.Key.Length == 0 || a.Key.Any(c => c is ';' or '=' or ' ') || a.Value.Contains(';', StringComparison.Ordinal)
                    || (a.Format == ListEntryFormat.CompatLayers && a.Value.Length > 0))
                {
                    add("value", $"List entry {a.Key}={a.Value} does not match format {a.Format}.");
                }

                break;
            case SystemParametersInfoAction a:
                CheckParameter(a.Parameter, a.Value, add);
                break;
        }
    }

    private static void CheckTaskPath(string taskPath, Action<string, string> add)
    {
        if (!ScheduledTaskPaths.IsAllowed(taskPath))
        {
            add("task-path", $"Task {taskPath} is not under \\Microsoft\\Windows\\ or contains characters that are not allowed.");
        }
    }

    private static void CheckParameter(string parameter, uint value, Action<string, string> add)
    {
        if (!SystemParameterWhitelist.All.TryGetValue(parameter, out var definition))
        {
            add("parameter", $"Parameter {parameter} is not on the allowlist.");
        }
        else if (value < definition.Min || value > definition.Max)
        {
            add("parameter", $"Value {value} is not allowed for {parameter} ({definition.Min} to {definition.Max}).");
        }
    }

    private static void LintConditionPaths(DetectCondition condition, Action<string, string> add)
    {
        switch (condition)
        {
            case RegistryValueEqualsCondition c:
                CheckPath(c.Hive, c.Path, add);
                break;
            case RegistryValueAbsentCondition c:
                CheckPath(c.Hive, c.Path, add);
                break;
            case RegistryKeyExistsCondition c:
                CheckPath(c.Hive, c.Path, add);
                break;
            case RegistryKeyAbsentCondition c:
                CheckPath(c.Hive, c.Path, add);
                break;
            case CommandQueryEqualsCondition c when !CommandWhitelist.All.ContainsKey(c.Command):
                add("command", $"Command {c.Command} is not on the allowlist.");
                break;
            case ScheduledTaskEnabledEqualsCondition c:
                CheckTaskPath(c.TaskPath, add);
                break;
            case SystemParameterEqualsCondition c:
                CheckParameter(c.Parameter, c.Value, add);
                break;
        }
    }

    /// <summary>Jede Aktion braucht eine passende Erkennungsbedingung mit demselben Zielwert und umgekehrt.</summary>
    private static void LintApplyMatchesDetect(TweakDefinition t, Action<string, string> add)
    {
        var leaves = DetectTypeNames.LeavesOf(t.Detect).ToList();
        var matched = new HashSet<DetectCondition>();

        foreach (var action in t.Apply)
        {
            var match = leaves.FirstOrDefault(leaf => !matched.Contains(leaf) && Matches(action, leaf));
            if (match is null)
            {
                add("apply-detect", $"Action {ActionTypeNames.NameOf(action)} has no matching condition in 'detect' with the same target value.");
            }
            else
            {
                matched.Add(match);
            }
        }

        foreach (var leaf in leaves.Where(l => !matched.Contains(l)))
        {
            add("apply-detect", $"Condition {DetectTypeNames.NameOf(leaf)} in 'detect' has no matching action.");
        }
    }

    private static bool Matches(TweakAction action, DetectCondition leaf)
    {
        return (action, leaf) switch
        {
            (RegistrySetValueAction a, RegistryValueEqualsCondition c) =>
                SameValue(a.Hive, a.Path, a.Name, c.Hive, c.Path, c.Name)
                && a.Value.TryToData(a.Kind, out var data, out _)
                && c.Value.Matches(data),
            (RegistryDeleteValueAction a, RegistryValueAbsentCondition c) =>
                SameValue(a.Hive, a.Path, a.Name, c.Hive, c.Path, c.Name),
            (RegistryCreateKeyAction a, RegistryKeyExistsCondition c) =>
                a.Hive == c.Hive && SamePath(a.Path, c.Path),
            (RegistryDeleteKeyAction a, RegistryKeyAbsentCondition c) =>
                a.Hive == c.Hive && SamePath(a.Path, c.Path),
            (ServiceSetStartTypeAction a, ServiceStartTypeEqualsCondition c) =>
                string.Equals(a.Service, c.Service, StringComparison.OrdinalIgnoreCase) && a.StartType == c.Value,
            (PowerPlanSetAction a, PowerPlanActiveCondition c) => a.SchemeGuid == c.SchemeGuid,
            (PowerSettingSetAction a, PowerSettingEqualsCondition c) => a.Subgroup == c.Subgroup && a.Setting == c.Setting && a.Value == c.Value,
            (CommandAction a, CommandQueryEqualsCondition c) =>
                string.Equals(a.Command, c.Command, StringComparison.Ordinal) && string.Equals(a.Value, c.Value, StringComparison.Ordinal),
            (ScheduledTaskSetEnabledAction a, ScheduledTaskEnabledEqualsCondition c) =>
                string.Equals(a.TaskPath, c.TaskPath, StringComparison.OrdinalIgnoreCase) && a.Enabled == c.Value,
            (RegistryListEntryAction a, RegistryListEntryEqualsCondition c) =>
                SameValue(a.Hive, a.Path, a.Name, c.Hive, c.Path, c.Name) && a.Format == c.Format
                && string.Equals(a.Key, c.Key, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Value, c.Value, StringComparison.Ordinal),
            (SystemParametersInfoAction a, SystemParameterEqualsCondition c) =>
                string.Equals(a.Parameter, c.Parameter, StringComparison.Ordinal) && a.Value == c.Value,
            (DisplayRefreshRateAction, DisplayRefreshIsHighestCondition) => true,
            _ => false,
        };
    }

    private static bool SameValue(RegHive h1, string p1, string n1, RegHive h2, string p2, string n2)
    {
        return h1 == h2 && SamePath(p1, p2) && string.Equals(n1, n2, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void CheckPath(RegHive hive, string path, Action<string, string> add)
    {
        if (!RegistryPaths.IsWellFormed(path, out var error))
        {
            add("registry-path", $"{RegistryPaths.Display(hive, path)}: {error}");
        }
    }

    private static void CheckKeyAllowlist(RegHive hive, string path, Action<string, string> add)
    {
        if (!RegistryPaths.IsKeyOperationAllowed(hive, path))
        {
            add("key-allowlist", $"{RegistryPaths.Display(hive, path)} is not below an allowed root.");
        }
    }
}
