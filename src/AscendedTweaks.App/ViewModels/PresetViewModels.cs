using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AscendedTweaks.App.Services;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace AscendedTweaks.App.ViewModels;

/// <summary>Speichert, welche Presets welche Tweaks wollen (pro Benutzer, nur lokal).</summary>
public interface IPresetOwnershipStore
{
    PresetOwnership Load();

    void Save(PresetOwnership ownership);
}

public sealed class PresetOwnershipStore : IPresetOwnershipStore
{
    private readonly string _path;

    public PresetOwnershipStore(string path) => _path = path;

    public static string DefaultPath =>
        Path.Combine(ProductInfo.DataDirectory(Environment.SpecialFolder.LocalApplicationData), "presets.json");

    public PresetOwnership Load()
    {
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_path)) is { } data)
            {
                return new PresetOwnership(data.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Beschaedigte Datei: neu beginnen; die Sicherungen der Engine bleiben davon unberuehrt.
        }

        return new PresetOwnership();
    }

    public void Save(PresetOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(ownership.Snapshot()));
    }
}

/// <summary>Klartext-Gruppen fuer die Vorschau: "Windows reagiert schneller (4)", "Weniger im Hintergrund (3)" ...</summary>
public static class PresetGroups
{
    public static string KeyFor(string category) => category switch
    {
        "Performance.Background" or "Performance.Services" or "Performance.Tasks" or "Debloat.Widgets" => "background",
        _ when category.StartsWith("Performance.", StringComparison.Ordinal) => "faster",
        _ when category.StartsWith("Privacy.", StringComparison.Ordinal) => "privacy",
        _ when category.StartsWith("Gaming.", StringComparison.Ordinal) => "games",
        _ when category.StartsWith("Updates.", StringComparison.Ordinal) => "updates",
        _ when category.StartsWith("Network.", StringComparison.Ordinal) => "network",
        _ when category.StartsWith("Disk.", StringComparison.Ordinal) => "storage",
        _ => "look",
    };

    public static IReadOnlyList<PreviewGroup> For(TweakWorkspace workspace, IReadOnlyList<string> ids, Func<string, string?>? note = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return ids
            .Select(id => workspace.Cards.FirstOrDefault(c => c.Tweak.Id == id))
            .Where(c => c is not null)
            .GroupBy(c => KeyFor(c!.Tweak.Category))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PreviewGroup(
                workspace.Localizer.Format("group." + g.Key, ("n", g.Count())),
                g.Select(c => note?.Invoke(c!.Tweak.Id) is { } extra ? $"{c!.PlainTitle} ({extra})" : c!.PlainTitle).ToList()))
            .ToList();
    }
}

public sealed record LevelOption(PresetLevel Level, string Label)
{
    /// <summary>Screenreader lesen bei ListBox-Eintraegen ToString vor; ohne Override waere das der Typname.</summary>
    public override string ToString() => Label;
}

/// <summary>Preset-Karte (V2, Abschnitt 4.1 und 4.2): Symbol, Name, ein Satz, Anzahl der Aenderungen, Stufe Sanft/Standard/Stark.</summary>
public sealed partial class PresetCardViewModel : ObservableObject
{
    private readonly Localizer _localizer;
    private readonly Func<PresetCardViewModel, Task> _apply;
    private readonly Func<PresetCardViewModel, Task> _undo;
    private readonly Func<PresetCardViewModel, Task>? _preview;
    private Dictionary<PresetLevel, IReadOnlyList<ProfilePlanRow>> _plans = [];
    private Dictionary<PresetLevel, int> _cleanupCounts = [];

    [ObservableProperty]
    private LevelOption? _selectedLevel;

    [ObservableProperty]
    private bool _isOwned;

    public PresetCardViewModel(TweakProfile profile, string name, string description, bool hasLevels, Localizer localizer, Func<PresetCardViewModel, Task> apply, Func<PresetCardViewModel, Task> undo, Func<PresetCardViewModel, Task>? preview = null)
    {
        Profile = profile;
        Name = name;
        Description = description;
        HasLevels = hasLevels;
        _localizer = localizer;
        _apply = apply;
        _undo = undo;
        _preview = preview;
        Symbol = Enum.TryParse<SymbolRegular>(profile.Symbol, out var symbol) ? symbol : SymbolRegular.Star24;
    }

    public TweakProfile Profile { get; }

    /// <summary>Schluessel fuer die Zuordnung "Preset rueckgaengig": vorgefertigt der Key, eigene Presets ihr Name.</summary>
    public string OwnerKey => Profile.Key ?? "own:" + Profile.Name;

    public string Name { get; }

    public string Description { get; }

    public SymbolRegular Symbol { get; }

    public bool HasLevels { get; }

    public bool IsCleanup => Profile.Cleanup.Count > 0;

    public ObservableCollection<LevelOption> Levels { get; } = [];

    public PresetLevel Level => SelectedLevel?.Level ?? PresetLevel.Standard;

    public IReadOnlyList<ProfilePlanRow> Plan => _plans.TryGetValue(Level, out var plan) ? plan : [];

    public int CleanupCount => _cleanupCounts.GetValueOrDefault(Level);

    public string Summary
    {
        get
        {
            if (IsCleanup)
            {
                return _localizer.Format("simple.preset.cleanup.summary", ("n", CleanupCount));
            }

            var changes = Plan.Count(r => r.Action is ProfileAction.Apply or ProfileAction.Revert);
            if (changes == 0)
            {
                return _localizer["simple.preset.alldone"];
            }

            var reboot = Plan.Any(r => r.RequiresReboot);
            return _localizer.Format("simple.preset.summary", ("n", changes), ("reboot", _localizer[reboot ? "dashboard.yes" : "dashboard.no"]));
        }
    }

    /// <summary>One click for settings; cleanup deletes files and therefore keeps its preview with sizes.</summary>
    public string ApplyText => _localizer[IsCleanup ? "simple.preset.review" : "simple.preset.apply"];

    public bool CanPreview => !IsCleanup && _preview is not null && Plan.Any(r => r.Action is ProfileAction.Apply or ProfileAction.Revert);

    /// <summary>What a click changes, written before the click: "Changes: A, B, C and 2 more".</summary>
    public string? ChangesText
    {
        get
        {
            var names = Plan.Where(r => r.Action is ProfileAction.Apply or ProfileAction.Revert).Select(r => r.Name).ToList();
            if (IsCleanup || names.Count == 0)
            {
                return null;
            }

            var list = names.Count <= 3
                ? string.Join(", ", names)
                : _localizer.Format("simple.preset.changes.more", ("list", string.Join(", ", names.Take(3))), ("n", names.Count - 3));
            return _localizer.Format("simple.preset.changes", ("list", list));
        }
    }

    public void SetPlans(Dictionary<PresetLevel, IReadOnlyList<ProfilePlanRow>> plans, Dictionary<PresetLevel, int> cleanupCounts)
    {
        _plans = plans;
        _cleanupCounts = cleanupCounts;
        var selected = Level;
        Levels.Clear();
        foreach (var level in HasLevels ? Enum.GetValues<PresetLevel>() : [PresetLevel.Standard])
        {
            var count = IsCleanup ? cleanupCounts.GetValueOrDefault(level) : plans.GetValueOrDefault(level)?.Count(r => r.Action is ProfileAction.Apply or ProfileAction.Revert) ?? 0;
            Levels.Add(new LevelOption(level, _localizer.Format("level." + level.ToString().ToLowerInvariant() + ".count", ("n", count))));
        }

        SelectedLevel = Levels.FirstOrDefault(l => l.Level == selected) ?? Levels.FirstOrDefault();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ChangesText));
        OnPropertyChanged(nameof(CanPreview));
    }

    partial void OnSelectedLevelChanged(LevelOption? value)
    {
        OnPropertyChanged(nameof(Plan));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ChangesText));
        OnPropertyChanged(nameof(CanPreview));
    }

    [RelayCommand]
    private Task Apply() => _apply(this);

    [RelayCommand]
    private Task Preview() => _preview?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand]
    private Task Undo() => _undo(this);
}

/// <summary>Eigenes Preset (V2, Abschnitt 4.1): Name, Haken setzen, speichern.</summary>
public sealed partial class CustomPresetDialogViewModel : DialogViewModel<TweakProfile?>
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    public CustomPresetDialogViewModel(IEnumerable<TweakCardViewModel> cards)
    {
        Items = cards.Select(c => new CustomPresetItem(c.Tweak.Id, c.PlainTitle, c.PlainDescription, () => SaveCommand.NotifyCanExecuteChanged())).ToList();
    }

    public IReadOnlyList<CustomPresetItem> Items { get; }

    private bool CanSave => ProfileCodec.IsValidName(Name.Trim()) && Items.Any(i => i.IsChecked);

    public override void Cancel() => Close(null);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => Close(new TweakProfile
    {
        Name = Name.Trim(),
        Tweaks = Items.Where(i => i.IsChecked).Select(i => new ProfileEntry { Id = i.Id }).ToList(),
    });

    [RelayCommand]
    private void Decline() => Close(null);
}

public sealed partial class CustomPresetItem : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private bool _isChecked;

    public CustomPresetItem(string id, string title, string description, Action changed)
    {
        Id = id;
        Title = title;
        Description = description;
        _changed = changed;
    }

    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    partial void OnIsCheckedChanged(bool value) => _changed();
}

/// <summary>
/// Start-Seite (V2, Abschnitte 2.5 und 4): ein Satz Status mit echten Messwerten, sieben Presets mit Stufen und eigene Presets.
/// One click applies a preset (the tile lists what changes); "Show changes" opens the full preview. Afterwards "Undo everything"
/// in the result or later "Undo preset". Cleanup keeps its preview with sizes because it deletes files.
/// </summary>
public sealed partial class SimpleStartViewModel : PageViewModel
{
    private readonly TweakWorkspace _workspace;
    private readonly ISystemInfoSource _system;
    private readonly Func<IReadOnlyList<AutostartEntry>> _autostart;
    private readonly IReadOnlyList<TweakProfile> _presets;
    private readonly IProfileStore? _ownPresets;
    private readonly IPresetOwnershipStore _ownership;
    private readonly ICleanupService? _cleanup;
    private readonly IDialogService _dialogs;
    private readonly bool _hasBattery;
    private readonly Localizer _localizer;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private string? _windowsText;

    [ObservableProperty]
    private string? _ramText;

    [ObservableProperty]
    private string? _autostartText;

    public SimpleStartViewModel(
        TweakWorkspace workspace,
        ISystemInfoSource system,
        Func<IReadOnlyList<AutostartEntry>> autostart,
        IReadOnlyList<TweakProfile> presets,
        bool hasBattery,
        IDialogService dialogs,
        IPresetOwnershipStore ownership,
        IProfileStore? ownPresets = null,
        ICleanupService? cleanup = null,
        MeasurementViewModel? measurement = null)
        : base("nav.simple.start", showHonestyBanner: false)
    {
        _workspace = workspace;
        _system = system;
        _autostart = autostart;
        _presets = presets;
        _hasBattery = hasBattery;
        _dialogs = dialogs;
        _ownership = ownership;
        _ownPresets = ownPresets;
        _cleanup = cleanup;
        _localizer = workspace.Localizer;
        Measurement = measurement;
        Help = new HelpViewModel("simple.start.help");
    }

    public HelpViewModel Help { get; }

    /// <summary>Karte "Messung" (V2 Abschnitt 5.4); fehlt in Tests ohne Messquelle.</summary>
    public MeasurementViewModel? Measurement { get; }

    public ObservableCollection<PresetCardViewModel> Presets { get; } = [];

    public bool CanCreateOwn => _ownPresets is not null && !_workspace.IsReadOnly;

    /// <summary>The start page has its own hero heading.</summary>
    public override bool ShowTitle => false;

    /// <summary>Main action of the hero: the "Safe and fast" preset, which opens the preview like every preset.</summary>
    public PresetCardViewModel? Recommended => Presets.FirstOrDefault(p => p.Profile.Key == "safe");

    public async Task LoadAsync()
    {
        await _workspace.RefreshAsync().ConfigureAwait(true);
        var (info, autostarts) = await Task.Run(() => (_system.Read(), CountAutostarts())).ConfigureAwait(true);
        var windows = info.Windows?.Edition ?? "Windows";
        var ram = info.Ram is { } r ? CleanupPageViewModel.Size(r.TotalBytes) : _localizer["anticheat.value.unknown"];
        Status = autostarts is { } n
            ? _localizer.Format("simple.start.status", ("windows", windows), ("ram", ram), ("n", n))
            : _localizer.Format("simple.start.status.noautostart", ("windows", windows), ("ram", ram));
        WindowsText = windows;
        RamText = ram;
        AutostartText = autostarts?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? _localizer["anticheat.value.unknown"];
        await LoadPresetsAsync().ConfigureAwait(true);
    }

    public async Task LoadPresetsAsync()
    {
        var cards = _presets
            .Select(p => new PresetCardViewModel(p, _localizer[$"preset.{p.Key}.name"], _localizer[$"preset.{p.Key}.desc"], hasLevels: true, _localizer, ApplyAsync, UndoAsync, PreviewAsync))
            .ToList();
        if (_ownPresets is not null)
        {
            cards.AddRange(_ownPresets.List().Select(s => new PresetCardViewModel(s.Profile with { Symbol = "Person24" }, s.Profile.Name, s.Profile.Description ?? _localizer["simple.preset.own.desc"], hasLevels: false, _localizer, ApplyAsync, UndoAsync, PreviewAsync)));
        }

        var ownership = _ownership.Load();
        foreach (var card in cards)
        {
            await PlanAsync(card).ConfigureAwait(true);
            card.IsOwned = ownership.Owns(card.OwnerKey);
        }

        Presets.Clear();
        foreach (var card in cards)
        {
            Presets.Add(card);
        }

        OnPropertyChanged(nameof(Recommended));
    }

    /// <summary>Plan je Stufe: Die Stufen-Regel waehlt die Eintraege, der bestehende ProfilePlanner prueft Zustand und Verfuegbarkeit.</summary>
    private async Task PlanAsync(PresetCardViewModel card)
    {
        var catalog = _workspace.Catalog;
        var plans = new Dictionary<PresetLevel, IReadOnlyList<ProfilePlanRow>>();
        var cleanup = new Dictionary<PresetLevel, int>();
        foreach (var level in card.HasLevels ? Enum.GetValues<PresetLevel>() : [PresetLevel.Standard])
        {
            var entries = card.Profile.Tweaks
                .Where(e => !card.HasLevels || (catalog.TryGet(e.Id, out var t) && PresetLevels.Includes(level, t, e, _hasBattery)))
                .ToList();
            var profile = card.Profile with { Tweaks = entries };
            plans[level] = entries.Count == 0
                ? []
                : await Task.Run(() => ProfilePlanner.Plan(profile, catalog, _workspace.Service.GetStatus, PlainName, _workspace.Text, _localizer, expertMode: false, _hasBattery)).ConfigureAwait(true);
            cleanup[level] = CleanupFor(card, level).Count;
        }

        card.SetPlans(plans, cleanup);
    }

    private List<CleanupCategory> CleanupFor(PresetCardViewModel card, PresetLevel level) =>
        CleanupCategories.All.Where(c => card.Profile.Cleanup.Contains(c.Id) && PresetLevels.IncludesCleanup(level, c)).ToList();

    private string PlainName(string id) => _workspace.Cards.FirstOrDefault(c => c.Tweak.Id == id)?.PlainTitle ?? _workspace.NameOf(id);

    private string? ConflictNote(string id, string ownerKey)
    {
        var others = _ownership.Load().OwnersOf(id).Where(o => o != ownerKey).ToList();
        return others.Count == 0 ? null : _localizer.Format("simple.preset.conflict", ("preset", PresetName(others[^1])));
    }

    private string PresetName(string ownerKey) => ownerKey.StartsWith("own:", StringComparison.Ordinal) ? ownerKey[4..] : _localizer[$"preset.{ownerKey}.name"];

    private async Task ApplyAsync(PresetCardViewModel card)
    {
        Message = null;
        if (card.IsCleanup)
        {
            await CleanupAsync(card).ConfigureAwait(true);
            return;
        }

        var plan = card.Plan;
        var applies = plan.Where(r => r.Action == ProfileAction.Apply).Select(r => r.Id).ToList();
        var reverts = plan.Where(r => r.Action == ProfileAction.Revert).Select(r => r.Id).ToList();
        var unchanged = plan.Where(r => r.Action == ProfileAction.Unchanged).Select(r => r.Id).ToList();
        if (applies.Count == 0 && reverts.Count == 0)
        {
            Claim(card.OwnerKey, unchanged, []);
            Message = _localizer["simple.preset.nothing"];
            await LoadPresetsAsync().ConfigureAwait(true);
            return;
        }

        if (Measurement is not null)
        {
            await Measurement.EnsureBeforeAsync().ConfigureAwait(true);
        }

        var summary = await _workspace.ApplyIdsAsync(applies, reverts, new BatchOptions(
            ids => PresetGroups.For(_workspace, ids, id => ConflictNote(id, card.OwnerKey)),
            UndoRunAsync)).ConfigureAwait(true);
        if (!summary.UndoRequested && (summary.Applied.Count > 0 || summary.Reverted.Count > 0))
        {
            Claim(card.OwnerKey, summary.Applied.Concat(unchanged), summary.Reverted);
            Measurement?.PresetApplied();
        }

        await LoadPresetsAsync().ConfigureAwait(true);
    }

    /// <summary>"Show changes": the full preview of the selected level, nothing is applied.</summary>
    private Task PreviewAsync(PresetCardViewModel card)
    {
        var applies = card.Plan.Where(r => r.Action == ProfileAction.Apply).Select(r => r.Id).ToList();
        var reverts = card.Plan.Where(r => r.Action == ProfileAction.Revert).Select(r => r.Id).ToList();
        return _workspace.PreviewIdsAsync(applies, reverts, new BatchOptions(ids => PresetGroups.For(_workspace, ids, id => ConflictNote(id, card.OwnerKey)), null));
    }

    private void Claim(string ownerKey, IEnumerable<string> applied, IReadOnlyList<string> reverted)
    {
        var ownership = _ownership.Load();
        ownership.Drop(reverted);
        ownership.Claim(ownerKey, applied);
        _ownership.Save(ownership);
    }

    /// <summary>"Alles wieder rueckgaengig" direkt im Ergebnis: genau diesen Lauf zuruecknehmen.</summary>
    private async Task UndoRunAsync(BatchSummary summary)
    {
        await _workspace.RevertManyAsync(summary.Applied).ConfigureAwait(true);
        if (summary.Reverted.Count > 0)
        {
            await _workspace.ApplyIdsAsync(summary.Reverted).ConfigureAwait(true);
        }
    }

    /// <summary>"Preset rueckgaengig": nur Tweaks, die kein anderes Preset mehr will (Abschnitt 4.3).</summary>
    private async Task UndoAsync(PresetCardViewModel card)
    {
        var ownership = _ownership.Load();
        var free = ownership.Release(card.OwnerKey);
        _ownership.Save(ownership);
        var tracked = free.Where(id => _workspace.Service.GetStatus(id).TrackedInLedger).ToList();
        if (tracked.Count > 0)
        {
            await _workspace.RevertManyAsync(tracked).ConfigureAwait(true);
        }
        else
        {
            Message = _localizer["simple.preset.undo.nothing"];
        }

        await LoadPresetsAsync().ConfigureAwait(true);
    }

    /// <summary>Preset "Aufraeumen": nur Bereinigung, keine Registry-Aenderung; zuerst Scan und Vorschau mit Groessen.</summary>
    private async Task CleanupAsync(PresetCardViewModel card)
    {
        if (_cleanup is null || _workspace.IsReadOnly)
        {
            return;
        }

        var categories = CleanupFor(card, card.Level);
        var scans = await Task.Run(() => categories.Select(c => (Category: c, Scan: _cleanup.ScanCleanup(c.Id))).ToList()).ConfigureAwait(true);
        var useful = scans.Where(s => s.Scan.Blocked is null && !s.Scan.NothingToDo).ToList();
        if (useful.Count == 0)
        {
            Message = _localizer["simple.preset.cleanup.nothing"];
            return;
        }

        var items = useful.Select(s => _localizer.Format("simple.preset.cleanup.item", ("name", _localizer[$"clean.{s.Category.Id}.name"]), ("size", s.Scan.SizeUnknown ? _localizer["anticheat.value.unknown"] : CleanupPageViewModel.Size(s.Scan.Bytes)))).ToList();
        var preview = new PreviewDialogViewModel([], [], [], false, useful.Any(s => s.Category.Scope != TweakScope.User), previewOnly: false)
        {
            Groups = [new PreviewGroup(_localizer.Format("group.cleanup", ("n", useful.Count)), items)],
        };
        if (!await _dialogs.ShowAsync(preview).ConfigureAwait(true))
        {
            return;
        }

        var rows = new List<ResultRow>();
        foreach (var (category, _) in useful)
        {
            var mode = category.SupportsRecycleBin ? CleanupMode.RecycleBin : CleanupMode.Permanent;
            try
            {
                var result = await Task.Run(() => _cleanup.RunCleanup(category.Id, mode)).ConfigureAwait(true);
                rows.Add(new ResultRow(_localizer[$"clean.{category.Id}.name"], result.Failed == 0 && result.Blocked is null, result.Blocked ?? CleanupPageViewModel.Size(result.DeletedBytes)));
            }
            catch (AscendedTweaksException ex)
            {
                rows.Add(new ResultRow(_localizer[$"clean.{category.Id}.name"], false, ex.Message));
            }
        }

        await _dialogs.ShowAsync(new ResultDialogViewModel(_localizer["result.title"], _localizer.Format("result.summary", ("ok", rows.Count(r => r.Ok)), ("failed", rows.Count(r => !r.Ok))), rows, false)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CreateOwn()
    {
        if (_ownPresets is null)
        {
            return;
        }

        var cards = _workspace.Cards.Where(c => c.IsVisible && c.State != TweakState.Unavailable).OrderBy(c => c.Tweak.Module, StringComparer.Ordinal).ThenBy(c => c.PlainTitle, StringComparer.CurrentCultureIgnoreCase);
        if (await _dialogs.ShowAsync(new CustomPresetDialogViewModel(cards)).ConfigureAwait(true) is { } profile)
        {
            _ownPresets.Save(profile);
            await LoadPresetsAsync().ConfigureAwait(true);
        }
    }

    private int? CountAutostarts()
    {
        try
        {
            return _autostart().Count(e => e.Enabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or AscendedTweaksException)
        {
            return null;
        }
    }
}
