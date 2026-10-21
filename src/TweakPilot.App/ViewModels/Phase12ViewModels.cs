using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweakPilot.App.Services;
using TweakPilot.Core;
using TweakPilot.Engine.Insights;
using TweakPilot.Localization;

namespace TweakPilot.App.ViewModels;

public sealed record InfoRow(string Label, string Value);

public sealed record IndicatorRow(string Label, string Value, string Explanation);

/// <summary>
/// Dashboard (Abschnitt 6.1, Experten-Modus): Systeminfos und messbare Zustandsanzeigen ohne Gesamtwert.
/// Die fruehere "Empfohlene Optimierung" ist seit V2 das Preset "Sicher &amp; Schnell" auf der Start-Seite.
/// </summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly TweakWorkspace _workspace;
    private readonly ISystemInfoSource _system;
    private readonly ISecurityFactsSource _security;
    private readonly Func<IReadOnlyList<AutostartEntry>> _autostart;
    private readonly Localizer _localizer;

    [ObservableProperty]
    private bool _isLoading;

    public DashboardViewModel(TweakWorkspace workspace, ISystemInfoSource system, ISecurityFactsSource security, Func<IReadOnlyList<AutostartEntry>> autostart)
        : base("nav.dashboard", showHonestyBanner: true)
    {
        _workspace = workspace;
        _system = system;
        _security = security;
        _autostart = autostart;
        _localizer = workspace.Localizer;
    }

    public ObservableCollection<InfoRow> System { get; } = [];

    public ObservableCollection<IndicatorRow> Indicators { get; } = [];

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await _workspace.RefreshAsync().ConfigureAwait(true);
            var trim = _workspace.Cards.FirstOrDefault(c => c.Tweak.Id == "DISK-001")?.State;
            var (info, health, security, autostarts) = await Task.Run(() => (_system.Read(), _system.ReadHealth(), _security.Read(), CountAutostarts())).ConfigureAwait(true);
            Fill(info, health, security, autostarts, trim);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private int? CountAutostarts()
    {
        try
        {
            return _autostart().Count(e => e.Enabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TweakPilotException)
        {
            return null;
        }
    }

    private void Fill(SystemInfo info, HealthFacts health, SecurityFacts security, int? autostarts, TweakState? trim)
    {
        var culture = CultureInfo.GetCultureInfo(_localizer.Culture);
        string Size(long? bytes) => bytes is { } b ? CleanupPageViewModel.Size(b) : Unknown;
        string Date(DateTimeOffset? date) => date?.ToLocalTime().ToString("d", culture) ?? Unknown;

        System.Clear();
        if (info.Cpu is { } cpu)
        {
            System.Add(new InfoRow(_localizer["dashboard.cpu"], _localizer.Format("dashboard.cpu.value", ("name", cpu.Name), ("cores", cpu.Cores), ("threads", cpu.Threads))));
        }

        foreach (var gpu in info.Gpus)
        {
            System.Add(new InfoRow(_localizer["dashboard.gpu"], _localizer.Format(
                "dashboard.gpu.value",
                ("name", gpu.Name.StartsWith(gpu.Vendor, StringComparison.OrdinalIgnoreCase) ? gpu.Name : $"{gpu.Vendor} {gpu.Name}"),
                ("memory", Size(gpu.MemoryBytes)),
                ("driver", gpu.DriverVersion ?? Unknown),
                ("date", gpu.DriverDate?.ToString("d", culture) ?? Unknown))));
        }

        if (info.Ram is { } ram)
        {
            System.Add(new InfoRow(_localizer["dashboard.ram"], _localizer.Format("dashboard.ram.value", ("total", Size(ram.TotalBytes)), ("type", ram.ModuleType ?? Unknown), ("used", Size(ram.UsedBytes)))));
        }

        foreach (var disk in info.Disks)
        {
            System.Add(new InfoRow(_localizer["dashboard.disk"], _localizer.Format("dashboard.disk.value", ("model", disk.Model), ("kind", _localizer["dashboard.disk." + disk.Kind]), ("size", Size(disk.SizeBytes)), ("free", Size(disk.FreeBytes)))));
        }

        if (info.Windows is { } windows)
        {
            System.Add(new InfoRow(_localizer["dashboard.windows"], _localizer.Format("dashboard.windows.value", ("edition", windows.Edition), ("version", windows.Version ?? Unknown), ("build", windows.Build), ("installed", Date(windows.InstalledUtc)))));
        }

        var duration = info.Boot.Duration is { } d
            ? _localizer.Format("dashboard.boot.duration", ("seconds", d.TotalSeconds.ToString("0.0", culture)))
            : _localizer[info.Boot.DurationNeedsAdmin ? "dashboard.boot.admin" : "dashboard.boot.unknown"];
        System.Add(new InfoRow(_localizer["dashboard.boot"], _localizer.Format("dashboard.boot.value", ("time", info.Boot.LastBootUtc?.ToLocalTime().ToString("g", culture) ?? Unknown), ("duration", duration))));

        Indicators.Clear();
        Indicators.Add(Row("autostart", autostarts?.ToString(culture) ?? Unknown));
        Indicators.Add(Row("free", Percent(health.SystemDriveFreePercent)));
        Indicators.Add(Row("ramload", Percent(health.RamLoadPercent)));
        Indicators.Add(Row("reboot", YesNo(health.RebootPending)));
        Indicators.Add(Row("trim", YesNo(trim switch { TweakState.Applied => true, TweakState.NotApplied => false, _ => null })));
        Indicators.Add(Row("secureboot", YesNo(security.SecureBoot)));
        Indicators.Add(Row("tpm", security.TpmVersion is { } tpm ? _localizer.Format("dashboard.tpm.version", ("version", tpm == 2 ? "2.0" : "1.2")) : _localizer["dashboard.tpm.none"]));
        Indicators.Add(Row("hvci", YesNo(security.MemoryIntegrityRunning)));
        Indicators.Add(Row("power", health.PowerPlan ?? Unknown));
        Indicators.Add(Row("gamemode", YesNo(health.GameMode)));
        Indicators.Add(Row("defender", health.DefenderRealtime is null
            ? Unknown
            : _localizer.Format(
                health.DefenderRealtime == true ? "dashboard.defender.on" : "dashboard.defender.off",
                ("products", health.Antivirus.Count > 0 ? string.Join(", ", health.Antivirus) : _localizer["dashboard.none"]))));
    }

    private string Unknown => _localizer["anticheat.value.unknown"];

    private IndicatorRow Row(string key, string value) => new(_localizer[$"dashboard.ind.{key}"], value, _localizer[$"dashboard.ind.{key}.why"]);

    private string YesNo(bool? value) => value switch
    {
        true => _localizer["dashboard.yes"],
        false => _localizer["dashboard.no"],
        null => Unknown,
    };

    private string Percent(int? value) => value is { } v ? v.ToString(CultureInfo.GetCultureInfo(_localizer.Culture)) + " %" : Unknown;
}
