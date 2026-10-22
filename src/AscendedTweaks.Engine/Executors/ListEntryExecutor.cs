using AscendedTweaks.Core;

namespace AscendedTweaks.Engine.Executors;

/// <summary>Ein Eintrag in einer Zeichenkette mit mehreren Einstellungen; fremde Eintraege bleiben erhalten.</summary>
internal sealed class RegistryListEntryExecutor : ActionExecutor<RegistryListEntryAction, RegistryListEntryCapture>
{
    private readonly IRegistryAccess _registry;

    public RegistryListEntryExecutor(IRegistryAccess registry) => _registry = registry;

    public string? ReadEntry(RegHive hive, string path, string name, ListEntryFormat format, string key)
    {
        return RegistryList.Get(_registry.GetValue(hive, path, name)?.Text, format, key);
    }

    protected override string TargetKey(RegistryListEntryAction action) =>
        $"{RegistryOps.ValueKey(action.Hive, action.Path, action.Name)}#{action.Key}".ToUpperInvariant();

    protected override RegistryListEntryCapture Capture(RegistryListEntryAction action)
    {
        var current = _registry.GetValue(action.Hive, action.Path, action.Name);
        if (current is not null && current.Kind is not (RegValueKind.String or RegValueKind.ExpandString))
        {
            throw new AscendedTweaksException(ErrorKind.Unavailable, $"{RegistryPaths.Display(action.Hive, action.Path)}\\{action.Name} is not a string.");
        }

        return new RegistryListEntryCapture
        {
            Hive = action.Hive,
            Path = action.Path,
            Name = action.Name,
            Format = action.Format,
            Key = action.Key,
            Before = StoredRegistryData.From(current),
            BeforeEntry = RegistryList.Get(current?.Text, action.Format, action.Key),
            MissingKeys = RegistryOps.MissingKeys(_registry, action.Hive, action.Path),
        };
    }

    protected override RegistryListEntryCapture Apply(RegistryListEntryAction action, RegistryListEntryCapture before)
    {
        var current = _registry.GetValue(action.Hive, action.Path, action.Name)?.Text;
        var text = RegistryList.Set(current, action.Format, action.Key, action.Value);
        _registry.SetValue(action.Hive, action.Path, action.Name, RegistryData.String(text));
        return before with { Applied = true, WrittenEntry = action.Value };
    }

    protected override bool Verify(RegistryListEntryAction action) =>
        ReadEntry(action.Hive, action.Path, action.Name, action.Format, action.Key) == action.Value;

    protected override void Restore(RegistryListEntryCapture capture)
    {
        var current = _registry.GetValue(capture.Hive, capture.Path, capture.Name);
        if (IsAtOriginalState(capture))
        {
            RegistryOps.RemoveCreatedKeys(_registry, capture.Hive, capture.MissingKeys);
            return;
        }

        var text = RegistryList.Set(current?.Text, capture.Format, capture.Key, capture.BeforeEntry);
        if (text.Length == 0)
        {
            if (capture.Before?.ToData().Text is { Length: 0 })
            {
                // Vorher existierte der Wert bereits leer: exakt so zuruecksetzen.
                RegistryOps.RestoreValue(_registry, capture.Hive, capture.Path, capture.Name, capture.Before);
                return;
            }

            // Keine Eintraege mehr: Wert entfernen statt eine leere Zeichenkette zu hinterlassen.
            if (current is not null)
            {
                _registry.DeleteValue(capture.Hive, capture.Path, capture.Name);
            }

            RegistryOps.RemoveCreatedKeys(_registry, capture.Hive, capture.MissingKeys);
            return;
        }

        _registry.SetValue(capture.Hive, capture.Path, capture.Name, RegistryData.String(text));
    }

    protected override bool IsAtWrittenState(RegistryListEntryCapture capture) =>
        ReadEntry(capture.Hive, capture.Path, capture.Name, capture.Format, capture.Key) == capture.WrittenEntry;

    /// <summary>
    /// Ursprung erreicht, wenn der eigene Eintrag wieder dem Vorher-Wert entspricht. Fehlte der Wert vorher, darf er nur
    /// noch existieren, wenn er fremde Eintraege enthaelt (zum Beispiel von einem anderen Tweak).
    /// </summary>
    protected override bool IsAtOriginalState(RegistryListEntryCapture capture)
    {
        var text = _registry.GetValue(capture.Hive, capture.Path, capture.Name)?.Text;
        return RegistryList.Get(text, capture.Format, capture.Key) == capture.BeforeEntry
            && (capture.Before is not null || text is null || RegistryList.Set(text, capture.Format, capture.Key, null).Length > 0);
    }

    protected override PlanItem Describe(string tweakId, RegistryListEntryAction action)
    {
        return new PlanItem
        {
            TweakId = tweakId,
            ActionType = NameOf(action),
            Target = RegistryPaths.Display(action.Hive, action.Path),
            ValueName = $"{action.Name} [{action.Key}]",
            Current = Display(ReadEntry(action.Hive, action.Path, action.Name, action.Format, action.Key), action.Format),
            New = Display(action.Value, action.Format),
        };
    }

    protected override CaptureView View(RegistryListEntryCapture capture)
    {
        return new CaptureView(
            $"{RegistryPaths.Display(capture.Hive, capture.Path)}\\{capture.Name} [{capture.Key}]",
            Display(ReadEntry(capture.Hive, capture.Path, capture.Name, capture.Format, capture.Key), capture.Format),
            Display(capture.BeforeEntry, capture.Format),
            Display(capture.WrittenEntry, capture.Format));
    }

    private static string Display(string? entry, ListEntryFormat format) => entry switch
    {
        null => EngineText.Missing,
        _ when format == ListEntryFormat.CompatLayers => "gesetzt",
        _ => entry,
    };
}
