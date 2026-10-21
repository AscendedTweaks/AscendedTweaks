using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweakPilot.App.Services;
using TweakPilot.Core;
using TweakPilot.Engine;
using TweakPilot.Localization;

namespace TweakPilot.App.ViewModels;

/// <summary>Stand von Windows Update (nur lesen): Pause laut Einstellungen und installierte Version.</summary>
public sealed record UpdateStatus(DateTimeOffset? PausedUntil, int Product, string? Release);

public interface IUpdateStatusSource
{
    UpdateStatus Read();
}

/// <summary>Updates (Abschnitt 6.9): Grundsatz, Pause, Zielversion und Link zu Windows Update. Die Richtlinien liegen im Bereich "Einstellungen".</summary>
public sealed partial class UpdatesOverviewViewModel : ObservableObject
{
    private readonly TweakWorkspace _workspace;
    private readonly IUpdateStatusSource _status;
    private readonly Action<string> _openUri;
    private readonly Func<DateTimeOffset> _now;

    [ObservableProperty]
    private int _selectedDays = 7;

    [ObservableProperty]
    private string? _pauseText;

    [ObservableProperty]
    private string? _versionText;

    private UpdateStatus? _current;

    public UpdatesOverviewViewModel(TweakWorkspace workspace, IUpdateStatusSource status, Action<string> openUri, Func<DateTimeOffset>? now = null)
    {
        _workspace = workspace;
        _status = status;
        _openUri = openUri;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<int> DayOptions { get; } = [7, 14, 21, 28, UpdateTweaks.MaxPauseDays];

    public string PauseTitle => _workspace.Text("tweak.UPD-001.name");

    public string HoldTitle => _workspace.Text("tweak.UPD-007.name");

    public string HoldSideEffects => _workspace.Text("tweak.UPD-007.side");

    /// <summary>Direktes Pausieren und Zielversion sind "Umstritten" bzw. Experte und deshalb nur im Expertenmodus bedienbar.</summary>
    public bool ExpertMode => _workspace.ExpertMode && !_workspace.IsReadOnly;

    public bool CanTarget => ExpertMode && _current?.Release is { } release && UpdateTweaks.IsValidRelease(release);

    public async Task LoadAsync()
    {
        _current = await Task.Run(_status.Read).ConfigureAwait(true);
        var localizer = _workspace.Localizer;
        var culture = CultureInfo.GetCultureInfo(localizer.Culture);
        PauseText = _current.PausedUntil is { } until && until > _now()
            ? localizer.Format("updates.pause.active", ("date", until.ToLocalTime().ToString("d", culture)))
            : localizer["updates.pause.none"];
        VersionText = localizer.Format("updates.version", ("product", _current.Product), ("release", _current.Release ?? localizer["anticheat.value.unknown"]));
        OnPropertyChanged(nameof(ExpertMode));
        OnPropertyChanged(nameof(CanTarget));
    }

    [RelayCommand]
    private void OpenWindowsUpdate() => _openUri("ms-settings:windowsupdate");

    [RelayCommand]
    private async Task Pause()
    {
        if (!ExpertMode || SelectedDays is < 1 or > UpdateTweaks.MaxPauseDays)
        {
            return;
        }

        await _workspace.ApplyIdsAsync([UpdateTweaks.PauseIdFor(DateOnly.FromDateTime(_now().UtcDateTime), SelectedDays)]).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task HoldVersion()
    {
        if (!CanTarget)
        {
            return;
        }

        await _workspace.ApplyIdsAsync([UpdateTweaks.TargetIdFor(_current!.Product, _current.Release!)]).ConfigureAwait(true);
    }
}

/// <summary>Messwerte fuer den Vorher/Nachher-Vergleich; null, wenn ein Wert nicht lesbar ist.</summary>
public sealed record ProfileMetrics(int? EnabledAutostarts, long? RamUsedBytes, long? FreeSystemBytes, int? Processes = null, TimeSpan? BootDuration = null);

public interface IProfileMetrics
{
    ProfileMetrics Capture();
}

/// <summary>Eigene Profile pro Benutzer.</summary>
public interface IProfileStore
{
    IReadOnlyList<StoredProfile> List();

    StoredProfile Save(TweakProfile profile, string? fileId = null);

    void Delete(string fileId);
}

public sealed record StoredProfile(string FileId, TweakProfile Profile);

/// <summary>Datei-Dialoge fuer Export und Import.</summary>
public interface IProfileFiles
{
    string? PickOpen();

    string? PickSave(string suggestedName);

    byte[] Read(string path);

    void Write(string path, byte[] data);

    /// <summary>Ordner fuer den Skript-Export; null bei Abbruch.</summary>
    string? PickFolder();
}

public enum ProfileAction
{
    Apply,
    Revert,
    Unchanged,
    Skip,
}

public sealed record ProfilePlanRow(string Id, string Name, ProfileAction Action, string Text, string? SideEffects, bool RequiresReboot);

/// <summary>
/// Plant ein Profil gegen den aktuellen Zustand: was angewendet, was zurueckgesetzt und was uebersprungen wird.
/// Versteckte Tweaks (Umstritten, Experte) nur im Expertenmodus; Zuruecksetzen nur fuer eigene Aenderungen der App.
/// </summary>
public static class ProfilePlanner
{
    public static IReadOnlyList<ProfilePlanRow> Plan(
        TweakProfile profile,
        ITweakCatalog catalog,
        Func<string, TweakStatus> status,
        Func<string, string> name,
        Func<string, string> text,
        Localizer localizer,
        bool expertMode,
        bool hasBattery)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var rows = new List<ProfilePlanRow>();
        foreach (var entry in profile.Tweaks)
        {
            if (!catalog.TryGet(entry.Id, out var tweak))
            {
                rows.Add(new ProfilePlanRow(entry.Id, entry.Id, ProfileAction.Skip, localizer["profiles.skip.unknown"], null, false));
                continue;
            }

            var displayName = name(entry.Id);
            var side = text(tweak.SideEffectsKey);
            ProfilePlanRow Row(ProfileAction action, string key) => new(entry.Id, displayName, action, localizer[key], action == ProfileAction.Apply ? side : null, tweak.RequiresReboot && action is ProfileAction.Apply or ProfileAction.Revert);

            if (entry.Condition == ProfileCondition.DesktopOnly && hasBattery)
            {
                rows.Add(Row(ProfileAction.Skip, "profiles.skip.battery"));
                continue;
            }

            // Zuruecksetzen auf den Originalwert ist immer erlaubt; nur das Anwenden versteckter Tweaks braucht den Expertenmodus.
            if (tweak.HiddenByDefault && !expertMode && entry.Target == ProfileTarget.Applied)
            {
                rows.Add(Row(ProfileAction.Skip, "profiles.skip.expert"));
                continue;
            }

            var current = status(entry.Id);
            if (current.State == TweakState.Unavailable)
            {
                rows.Add(new ProfilePlanRow(entry.Id, displayName, ProfileAction.Skip, current.Reason ?? localizer["profiles.skip.unavailable"], null, false));
                continue;
            }

            if (entry.Target == ProfileTarget.Applied)
            {
                rows.Add(current.State == TweakState.Applied ? Row(ProfileAction.Unchanged, "profiles.state.already") : Row(ProfileAction.Apply, "profiles.state.apply"));
            }
            else if (current.State == TweakState.NotApplied)
            {
                rows.Add(Row(ProfileAction.Unchanged, "profiles.state.alreadyoff"));
            }
            else
            {
                rows.Add(current.TrackedInLedger ? Row(ProfileAction.Revert, "profiles.state.revert") : Row(ProfileAction.Skip, "profiles.skip.foreign"));
            }
        }

        return rows;
    }
}

public sealed record ProfileItem(TweakProfile Profile, string? FileId, string Name, string? Description, string CountText)
{
    public bool IsOwn => FileId is not null;

    /// <summary>Fuer Screenreader (Listeneintrag): der Profilname.</summary>
    public override string ToString() => Name;
}

public sealed record ComparisonRow(string Tweak, string Before, string After, string Reboot);

public sealed record MetricRow(string Label, string Before, string After);

/// <summary>Profile (Abschnitt 6.10): vorgefertigt und eigene, Vorschau, Anwenden, Vorher/Nachher, Import und Export.</summary>
public sealed partial class ProfilesPageViewModel : PageViewModel
{
    private readonly TweakWorkspace _workspace;
    private readonly ITweakCatalog _catalog;
    private readonly IReadOnlyList<TweakProfile> _builtIn;
    private readonly IProfileStore _store;
    private readonly IProfileFiles _files;
    private readonly IProfileMetrics _metrics;
    private readonly IDialogService _dialogs;
    private readonly bool _hasBattery;
    private readonly Localizer _localizer;
    private int _planVersion;

    [ObservableProperty]
    private ProfileItem? _selected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _message;

    public ProfilesPageViewModel(
        TweakWorkspace workspace,
        ITweakCatalog catalog,
        IReadOnlyList<TweakProfile> builtIn,
        IProfileStore store,
        IProfileFiles files,
        IProfileMetrics metrics,
        IDialogService dialogs,
        bool hasBattery)
        : base("nav.profiles", showHonestyBanner: true)
    {
        _workspace = workspace;
        _catalog = catalog;
        _builtIn = builtIn;
        _store = store;
        _files = files;
        _metrics = metrics;
        _dialogs = dialogs;
        _hasBattery = hasBattery;
        _localizer = workspace.Localizer;
        Reload();
    }

    public ObservableCollection<ProfileItem> Profiles { get; } = [];

    public ObservableCollection<ProfilePlanRow> Plan { get; } = [];

    public ObservableCollection<ComparisonRow> Comparison { get; } = [];

    public ObservableCollection<MetricRow> Metrics { get; } = [];

    public bool HasComparison => Comparison.Count > 0 || Metrics.Count > 0;

    public bool CanChange => !_workspace.IsReadOnly;

    public bool CanApply => CanChange && Plan.Any(r => r.Action is ProfileAction.Apply or ProfileAction.Revert);

    public string PlanSummary => _localizer.Format(
        "profiles.plan.summary",
        ("apply", Plan.Count(r => r.Action == ProfileAction.Apply)),
        ("revert", Plan.Count(r => r.Action == ProfileAction.Revert)),
        ("unchanged", Plan.Count(r => r.Action == ProfileAction.Unchanged)),
        ("skip", Plan.Count(r => r.Action == ProfileAction.Skip)));

    partial void OnSelectedChanged(ProfileItem? value)
    {
        Message = null;
        _ = BuildPlanAsync();
    }

    /// <summary>Plant das gewaehlte Profil; ueberholte Planungen (schneller Wechsel der Auswahl) werden verworfen.</summary>
    public async Task BuildPlanAsync()
    {
        var version = ++_planVersion;
        IReadOnlyList<ProfilePlanRow> rows = [];
        if (Selected is { } item)
        {
            rows = await Task.Run(() => ProfilePlanner.Plan(
                item.Profile, _catalog, _workspace.Service.GetStatus, _workspace.NameOf, _workspace.Text, _localizer, _workspace.ExpertMode, _hasBattery)).ConfigureAwait(true);
        }

        if (version != _planVersion)
        {
            return;
        }

        Plan.Clear();
        foreach (var row in rows)
        {
            Plan.Add(row);
        }

        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(PlanSummary));
    }

    [RelayCommand]
    private async Task Apply()
    {
        if (!CanApply || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var changing = Plan.Where(r => r.Action is ProfileAction.Apply or ProfileAction.Revert).ToList();
            var before = await Task.Run(() => (Metrics: _metrics.Capture(), States: changing.ToDictionary(r => r.Id, r => _workspace.Service.GetStatus(r.Id).State))).ConfigureAwait(true);

            await _workspace.ApplyIdsAsync(
                changing.Where(r => r.Action == ProfileAction.Apply).Select(r => r.Id).ToList(),
                changing.Where(r => r.Action == ProfileAction.Revert).Select(r => r.Id).ToList()).ConfigureAwait(true);

            var after = await Task.Run(() => (Metrics: _metrics.Capture(), States: changing.ToDictionary(r => r.Id, r => _workspace.Service.GetStatus(r.Id).State))).ConfigureAwait(true);
            Comparison.Clear();
            foreach (var row in changing)
            {
                Comparison.Add(new ComparisonRow(
                    row.Name,
                    StateText(before.States[row.Id]),
                    StateText(after.States[row.Id]),
                    _localizer[row.RequiresReboot && before.States[row.Id] != after.States[row.Id] ? "profiles.reboot.yes" : "profiles.reboot.no"]));
            }

            Metrics.Clear();
            Metrics.Add(new MetricRow(_localizer["profiles.metric.autostart"], Number(before.Metrics.EnabledAutostarts), Number(after.Metrics.EnabledAutostarts)));
            Metrics.Add(new MetricRow(_localizer["profiles.metric.ram"], Bytes(before.Metrics.RamUsedBytes), Bytes(after.Metrics.RamUsedBytes)));
            Metrics.Add(new MetricRow(_localizer["profiles.metric.disk"], Bytes(before.Metrics.FreeSystemBytes), Bytes(after.Metrics.FreeSystemBytes)));
            OnPropertyChanged(nameof(HasComparison));
            await BuildPlanAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Speichert alle sichtbaren Katalog-Tweaks, die gerade aktiv sind, als eigenes Profil.</summary>
    [RelayCommand]
    private async Task SaveCurrent()
    {
        var name = await AskName(_localizer["profiles.save.title"], string.Empty).ConfigureAwait(true);
        if (name is null)
        {
            return;
        }

        var applied = await Task.Run(() => _workspace.Cards
            .Where(c => c.IsVisible && c.State == TweakState.Applied)
            .Select(c => new ProfileEntry { Id = c.Tweak.Id })
            .ToList()).ConfigureAwait(true);
        if (applied.Count == 0)
        {
            Message = _localizer["profiles.save.empty"];
            return;
        }

        var stored = _store.Save(new TweakProfile { Name = name, Tweaks = applied });
        Reload(stored.FileId);
        Message = _localizer.Format("profiles.save.done", ("n", applied.Count));
    }

    [RelayCommand]
    private async Task Rename()
    {
        if (Selected is not { IsOwn: true } item)
        {
            return;
        }

        var name = await AskName(_localizer["profiles.rename.title"], item.Name).ConfigureAwait(true);
        if (name is null)
        {
            return;
        }

        _store.Save(item.Profile with { Name = name }, item.FileId);
        Reload(item.FileId);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected is not { IsOwn: true } item || !await _dialogs.ShowAsync(new ConfirmDialogViewModel(
                _localizer["profiles.delete.title"], _localizer.Format("profiles.delete.body", ("name", item.Name)), _localizer["profiles.delete"], _localizer["dialog.cancel"], danger: true)).ConfigureAwait(true))
        {
            return;
        }

        _store.Delete(item.FileId!);
        Reload();
    }

    [RelayCommand]
    private void Export()
    {
        if (Selected is not { } item || _files.PickSave(ProfileCodec.FileNameFor(item.Name)) is not { } path)
        {
            return;
        }

        // Vorgefertigte Profile werden mit ihrem angezeigten Namen als normales Profil exportiert.
        var profile = item.Profile with { Key = null, Name = item.Name, Description = item.Description };
        try
        {
            _files.Write(path, ProfileCodec.Serialize(profile));
            Message = _localizer["profiles.export.done"];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = _localizer["profiles.export.failed"] + " " + ex.Message;
        }
    }

    /// <summary>
    /// Export als PowerShell-Skript und, falls moeglich, als .reg-Datei, jeweils mit Gegenstueck zum Zuruecksetzen (Abschnitt 10.4).
    /// Die Originalwerte sind die Werte dieses Rechners zum Zeitpunkt des Exports.
    /// </summary>
    [RelayCommand]
    private async Task ExportScripts()
    {
        if (Selected is not { } item || _files.PickFolder() is not { } folder)
        {
            return;
        }

        var ids = item.Profile.Tweaks.Where(e => e.Target == ProfileTarget.Applied).Select(e => e.Id).ToList();
        try
        {
            var export = await Task.Run(() => _workspace.Service.ExportScripts(ids, _workspace.NameOf, item.Name)).ConfigureAwait(true);
            var baseName = Path.GetFileNameWithoutExtension(ProfileCodec.FileNameFor(item.Name));
            var bom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            _files.Write(Path.Combine(folder, baseName + ".ps1"), bom.GetPreamble().Concat(bom.GetBytes(export.ApplyPs1)).ToArray());
            _files.Write(Path.Combine(folder, baseName + "-revert.ps1"), bom.GetPreamble().Concat(bom.GetBytes(export.RevertPs1)).ToArray());
            if (export.ApplyReg is not null)
            {
                var unicode = System.Text.Encoding.Unicode;
                _files.Write(Path.Combine(folder, baseName + ".reg"), unicode.GetPreamble().Concat(unicode.GetBytes(export.ApplyReg)).ToArray());
                _files.Write(Path.Combine(folder, baseName + "-revert.reg"), unicode.GetPreamble().Concat(unicode.GetBytes(export.RevertReg!)).ToArray());
            }

            Message = _localizer.Format("profiles.script.done", ("n", export.Exported.Count), ("skipped", export.Skipped.Count));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = _localizer["profiles.export.failed"] + " " + ex.Message;
        }
    }

    /// <summary>Import ist nicht vertrauenswuerdig: Pruefung durch ProfileCodec, danach nur Vorschau; angewendet wird erst auf Knopfdruck.</summary>
    [RelayCommand]
    private void Import()
    {
        if (_files.PickOpen() is not { } path)
        {
            return;
        }

        try
        {
            var data = _files.Read(path);
            var profile = ProfileCodec.Parse(data, _catalog);
            var stored = _store.Save(profile);
            Reload(stored.FileId);
            Message = _localizer["profiles.import.done"];
        }
        catch (TweakPilotException ex)
        {
            Message = _localizer["profiles.import.failed"] + " " + ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = _localizer["profiles.import.failed"] + " " + ex.Message;
        }
    }

    private void Reload(string? select = null)
    {
        var previous = select ?? Selected?.FileId ?? Selected?.Profile.Key;
        Profiles.Clear();
        foreach (var profile in _builtIn)
        {
            Profiles.Add(new ProfileItem(profile, null, _localizer[$"preset.{profile.Key}.name"], _localizer[$"preset.{profile.Key}.desc"], Count(profile)));
        }

        foreach (var stored in _store.List())
        {
            Profiles.Add(new ProfileItem(stored.Profile, stored.FileId, stored.Profile.Name, stored.Profile.Description, Count(stored.Profile)));
        }

        Selected = Profiles.FirstOrDefault(p => (p.FileId ?? p.Profile.Key) == previous) ?? Profiles.FirstOrDefault();
    }

    private string Count(TweakProfile profile) => _localizer.Format("profiles.count", ("n", profile.Tweaks.Count));

    private async Task<string?> AskName(string title, string initial)
    {
        var name = await _dialogs.ShowAsync(new TextInputDialogViewModel(
            title, _localizer["profiles.name"], initial, _localizer["dialog.save"], _localizer["dialog.cancel"], ProfileCodec.IsValidName, _localizer["profiles.name.invalid"])).ConfigureAwait(true);
        return name?.Trim();
    }

    private string StateText(TweakState state) => _localizer["profiles.tweakstate." + state];

    private static string Number(int? value) => value?.ToString(CultureInfo.CurrentCulture) ?? "-";

    private static string Bytes(long? value) => value is { } v ? CleanupPageViewModel.Size(v) : "-";
}
