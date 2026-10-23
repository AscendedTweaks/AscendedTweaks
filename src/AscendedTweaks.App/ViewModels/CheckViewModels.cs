using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AscendedTweaks.App.Controls;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Engine.Windows;
using AscendedTweaks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AscendedTweaks.App.ViewModels;

/// <summary>Sammelt die Fakten fuer die Gaming-Checks aus den vorhandenen Quellen; jede Quelle darf einzeln fehlen.</summary>
public sealed class GamingFactsReader
{
    private readonly ISystemInfoSource _system;
    private readonly IHardwareFacts _hardware;
    private readonly Func<Guid?> _activePlan;
    private readonly Func<bool> _hasBattery;
    private readonly Func<IReadOnlyList<AutostartEntry>> _autostart;
    private readonly IProcessSampler _processes;
    private readonly ISecurityFactsSource _security;
    private readonly TimeProvider _time;

    public GamingFactsReader(
        ISystemInfoSource system,
        IHardwareFacts hardware,
        Func<Guid?> activePlan,
        Func<bool> hasBattery,
        Func<IReadOnlyList<AutostartEntry>> autostart,
        IProcessSampler processes,
        ISecurityFactsSource security,
        TimeProvider time)
    {
        _system = system;
        _hardware = hardware;
        _activePlan = activePlan;
        _hasBattery = hasBattery;
        _autostart = autostart;
        _processes = processes;
        _security = security;
        _time = time;
    }

    public GamingFacts Read()
    {
        var info = Safe(_system.Read);
        var health = Safe(_system.ReadHealth);
        var sample = Safe(_processes.Sample) ?? [];
        return new GamingFacts
        {
            Display = Safe(_hardware.ReadDisplay),
            ActivePowerPlan = Safe(_activePlan),
            HasBattery = Safe(() => (bool?)_hasBattery()) ?? false,
            Gpus = info?.Gpus ?? [],
            MemoryModules = Safe(_hardware.ReadMemoryModules),
            SystemDriveFreePercent = health?.SystemDriveFreePercent,
            GameMode = health?.GameMode,
            Autostarts = Safe(() => (int?)_autostart().Count(e => e.Enabled)),

            // Groesster Verbraucher, der kein Teil von Windows ist (Kern, Dienste, Oberflaeche).
            TopProcess = ProcessOverview.Compute(sample, sample, TimeSpan.FromSeconds(1), 1).ByMemory
                .FirstOrDefault(g => !KnownProcesses.Windows.ContainsKey(g.Name)),
            Security = Safe(_security.Read),
            Today = DateOnly.FromDateTime(_time.GetLocalNow().Date),
        };
    }

    private static T? Safe<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
#pragma warning disable CA1031 // Reine Anzeige: ein Lesefehler macht nur diesen einen Check "unbekannt".
        catch (Exception)
#pragma warning restore CA1031
        {
            return default;
        }
    }
}

public sealed record CheckRow(string Title, BadgeTone Tone, string StateText, string Text, string? HowTo, string? Link, GamingJump Jump, bool CanJump, string? LinkText,
    string? FixId = null, string? FixText = null);

/// <summary>
/// Gaming-Checks (V2 Abschnitt 5.3): ein Satz Ergebnis, "Behoben?"-Anleitung, Link oder Sprung, und "Fix now", wo ein Katalog-Tweak
/// den Befund behebt (mit dem Namen der Aenderung vor dem Klick). Keine stillen Aenderungen.
/// </summary>
public sealed partial class GamingChecksViewModel : ObservableObject
{
    private readonly Func<GamingFacts> _read;
    private readonly Localizer _localizer;
    private readonly Action<string> _open;

    [ObservableProperty]
    private bool _isLoading;

    public GamingChecksViewModel(Func<GamingFacts> read, Localizer localizer, Action<string> open)
    {
        _read = read;
        _localizer = localizer;
        _open = open;
    }

    public ObservableCollection<CheckRow> Rows { get; } = [];

    /// <summary>Springt zu einer Seite oder einem Bereich; gesetzt von der Seite, die die Checks zeigt.</summary>
    public Action<GamingJump>? Navigate { get; set; }

    /// <summary>Ziele, die von hier aus erreichbar sind (im Einfach-Modus zum Beispiel kein Anti-Cheat-Bereich).</summary>
    public IReadOnlySet<GamingJump> Targets { get; set; } = new HashSet<GamingJump> { GamingJump.Cleanup, GamingJump.Autostart };

    /// <summary>Applies the fixing tweak (backup, administrator prompt and result as usual); without it no "Fix now" button.</summary>
    public Func<string, Task>? Fix { get; set; }

    /// <summary>Name of a tweak for "Changes: ..." before the click.</summary>
    public Func<string, string>? NameOf { get; set; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var facts = await Task.Run(_read).ConfigureAwait(true);
            Rows.Clear();
            foreach (var check in GamingChecks.Evaluate(facts))
            {
                var tone = check.State switch
                {
                    CheckState.Good => BadgeTone.Success,
                    CheckState.Attention => BadgeTone.Warning,
                    _ => BadgeTone.Neutral,
                };
                var args = check.Args.Select(a => (a.Name, a.Value)).ToArray();
                Rows.Add(new CheckRow(
                    _localizer[$"check.{check.Id}.title"],
                    tone,
                    _localizer["check.state." + check.State],
                    _localizer.Format(check.TextKey, args),
                    check.HowToKey is { } how ? _localizer[how] : null,
                    check.Link,
                    check.Jump,
                    check.Jump != GamingJump.None && Navigate is not null && Targets.Contains(check.Jump),
                    check.Link is null ? null : _localizer[check.Link.StartsWith("ms-settings:", StringComparison.Ordinal) ? "check.link.settings" : "check.link.vendor"],
                    Fix is null ? null : check.FixTweakId,
                    Fix is null || check.FixTweakId is not { } fix ? null : _localizer.Format("check.fix.what", ("name", NameOf?.Invoke(fix) ?? fix))));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenLink(CheckRow row)
    {
        if (row?.Link is { } link && (link.StartsWith("ms-settings:", StringComparison.Ordinal) || link.StartsWith("https://", StringComparison.Ordinal)))
        {
            _open(link);
        }
    }

    [RelayCommand]
    private async Task FixAsync(CheckRow row)
    {
        if (row?.FixId is { } id && Fix is { } fix)
        {
            await fix(id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void Jump(CheckRow row)
    {
        if (row is { CanJump: true })
        {
            Navigate?.Invoke(row.Jump);
        }
    }
}

/// <summary>Ein Messpunkt: Zeitpunkt und Werte.</summary>
public sealed record MeasurementSnapshot(DateTimeOffset TakenUtc, ProfileMetrics Metrics);

/// <summary>Gespeicherter Stand der Karte "Messung" (nur lokal).</summary>
public sealed record MeasurementState
{
    public MeasurementSnapshot? Before { get; init; }

    /// <summary>Zeitpunkt, zu dem nach "Vorher" ein Preset angewendet wurde; "Nachher" gibt es erst nach dem naechsten Neustart.</summary>
    public DateTimeOffset? PresetAppliedUtc { get; init; }

    public MeasurementSnapshot? After { get; init; }
}

public interface IMeasurementStore
{
    MeasurementState Load();

    void Save(MeasurementState state);
}

public sealed class MeasurementStore : IMeasurementStore
{
    private readonly string _path;

    public MeasurementStore(string path) => _path = path;

    public static string DefaultPath =>
        Path.Combine(ProductInfo.DataDirectory(Environment.SpecialFolder.LocalApplicationData), "measurements.json");

    public MeasurementState Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<MeasurementState>(File.ReadAllText(_path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new();
        }
    }

    public void Save(MeasurementState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(state));
    }
}

public sealed record MeasureRow(string Label, string Value);

/// <summary>
/// Karte "Messung" (V2 Abschnitt 5.4): "Jetzt messen" erfasst Autostarts, belegten RAM, freien Speicher, Prozesse und die letzte
/// Startdauer (falls lesbar). Nach einem Preset bietet die Karte nach dem naechsten Neustart "Nachher messen" an und zeigt die
/// Differenz. Erweitert das Vorher/Nachher aus Phase 11 (IProfileMetrics) und speichert nur lokal.
/// </summary>
public sealed partial class MeasurementViewModel : ObservableObject
{
    private readonly IProfileMetrics _metrics;
    private readonly IMeasurementStore _store;
    private readonly Func<DateTimeOffset> _lastBoot;
    private readonly Localizer _localizer;
    private readonly TimeProvider _time;

    [ObservableProperty]
    private bool _isMeasuring;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MeasureAfterCommand))]
    private bool _canMeasureAfter;

    public MeasurementViewModel(IProfileMetrics metrics, IMeasurementStore store, Func<DateTimeOffset> lastBoot, Localizer localizer, TimeProvider time)
    {
        _metrics = metrics;
        _store = store;
        _lastBoot = lastBoot;
        _localizer = localizer;
        _time = time;
        Show(_store.Load());
    }

    public ObservableCollection<MeasureRow> Rows { get; } = [];

    /// <summary>Uptime-basiert: letzter Start = jetzt minus Laufzeit seit dem Start.</summary>
    public static DateTimeOffset LastBootFromUptime() => DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    [RelayCommand]
    private async Task MeasureNow()
    {
        var snapshot = await CaptureAsync().ConfigureAwait(true);
        var state = new MeasurementState { Before = snapshot };
        _store.Save(state);
        Show(state);
    }

    [RelayCommand(CanExecute = nameof(CanMeasureAfter))]
    private async Task MeasureAfter()
    {
        var snapshot = await CaptureAsync().ConfigureAwait(true);
        var state = _store.Load() with { After = snapshot };
        _store.Save(state);
        Show(state);
    }

    /// <summary>Vor dem Anwenden eines Presets: gibt es noch kein "Vorher", wird es jetzt erfasst.</summary>
    public async Task EnsureBeforeAsync()
    {
        var state = _store.Load();
        if (state.Before is null || state.After is not null)
        {
            state = new MeasurementState { Before = await CaptureAsync().ConfigureAwait(true) };
            _store.Save(state);
        }
    }

    /// <summary>Nach dem Anwenden eines Presets: "Nachher messen" wird nach dem naechsten Neustart angeboten.</summary>
    public void PresetApplied()
    {
        var state = _store.Load();
        if (state.Before is not null)
        {
            state = state with { PresetAppliedUtc = _time.GetUtcNow(), After = null };
            _store.Save(state);
            Show(state);
        }
    }

    private async Task<MeasurementSnapshot> CaptureAsync()
    {
        IsMeasuring = true;
        try
        {
            return new MeasurementSnapshot(_time.GetUtcNow(), await Task.Run(_metrics.Capture).ConfigureAwait(true));
        }
        finally
        {
            IsMeasuring = false;
        }
    }

    private void Show(MeasurementState state)
    {
        Rows.Clear();
        CanMeasureAfter = state.Before is not null && state.After is null && state.PresetAppliedUtc is { } applied && _lastBoot() > applied;
        if (state.Before is not { } before)
        {
            Status = _localizer["measure.none"];
            return;
        }

        var culture = CultureInfo.GetCultureInfo(_localizer.Culture);
        var b = before.Metrics;
        var a = state.After?.Metrics;
        Add("measure.autostart", Number(b.EnabledAutostarts), a is null ? null : Number(a.EnabledAutostarts));
        Add("measure.ram", Gigabytes(b.RamUsedBytes, culture), a is null ? null : Gigabytes(a.RamUsedBytes, culture));
        Add("measure.free", Gigabytes(b.FreeSystemBytes, culture), a is null ? null : Gigabytes(a.FreeSystemBytes, culture));
        Add("measure.processes", Number(b.Processes), a is null ? null : Number(a.Processes));
        if (b.BootDuration is not null || a?.BootDuration is not null)
        {
            Add("measure.boot", Seconds(b.BootDuration, culture), a is null ? null : Seconds(a.BootDuration, culture));
        }

        var when = before.TakenUtc.ToLocalTime().ToString("g", culture);
        Status = state.After is not null
            ? _localizer.Format("measure.compared", ("when", when))
            : CanMeasureAfter
                ? _localizer["measure.after.ready"]
                : state.PresetAppliedUtc is not null
                    ? _localizer["measure.after.reboot"]
                    : _localizer.Format("measure.before", ("when", when));
    }

    private void Add(string key, string before, string? after) =>
        Rows.Add(new MeasureRow(_localizer[key], after is null ? before : _localizer.Format("measure.change", ("before", before), ("after", after))));

    private string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? _localizer["anticheat.value.unknown"];

    private string Gigabytes(long? bytes, CultureInfo culture) =>
        bytes is { } v ? string.Create(culture, $"{v / 1024.0 / 1024 / 1024:0.0} GB") : _localizer["anticheat.value.unknown"];

    private string Seconds(TimeSpan? duration, CultureInfo culture) =>
        duration is { } d ? string.Create(culture, $"{d.TotalSeconds:0} s") : _localizer["anticheat.value.unknown"];
}
