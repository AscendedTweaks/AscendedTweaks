using System.Collections.ObjectModel;
using System.Globalization;
using AscendedTweaks.App.Services;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Tools;
using AscendedTweaks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AscendedTweaks.App.ViewModels;

public interface INetworkTools
{
    Task<PingStats> PingAsync(string target, IProgress<int>? progress, CancellationToken cancellation);

    bool FlushDnsCache();

    DohStatus Doh();

    MaintenanceResult NetworkReset();

    void OpenDnsSettings();
}

public interface IDiskTools
{
    IReadOnlyList<VolumeInfo> Volumes();

    MaintenanceResult Run(MaintenanceAction action, char drive);

    IReadOnlyList<DiskHealth> Health();

    /// <summary>Grosse Dateien klassifiziert (V2 Abschnitt 6); nur lesend, mit Zeitlimit und Fortschritt (Ordner).</summary>
    LargeFileScan ScanLargeFiles(long threshold, IReadOnlyList<string> keep, IProgress<int> progress, CancellationToken cancellation);

    /// <summary>Nur in den Papierkorb, mit erneuter Pruefung jeder Datei; Spielordner aus dem Scan sind gesperrt.</summary>
    RecycleOutcome RecycleLargeFiles(IReadOnlyList<LargeFileItem> items, IReadOnlyList<string> gameRoots);

    void ShowInExplorer(string path);
}

/// <summary>Bereichsseite mit Tweak-Karten und weiteren Bereichen (gemeinsam fuer Netzwerk und Datentraeger).</summary>
public sealed partial class SectionPageViewModel : PageViewModel
{
    private readonly Dictionary<string, Func<object>> _factories;
    private readonly Dictionary<string, object> _created = [];

    [ObservableProperty]
    private SectionItem _selectedSection;

    [ObservableProperty]
    private object? _currentSection;

    public SectionPageViewModel(string titleKey, IReadOnlyList<(SectionItem Section, Func<object> Create)> sections)
        : base(titleKey, showHonestyBanner: true)
    {
        Sections = sections.Select(s => s.Section).ToList();
        _factories = sections.ToDictionary(s => s.Section.Key, s => s.Create, StringComparer.Ordinal);
        _selectedSection = Sections[0];
        CurrentSection = Section(Sections[0].Key);
    }

    public IReadOnlyList<SectionItem> Sections { get; }

    partial void OnSelectedSectionChanged(SectionItem value)
    {
        if (value is not null)
        {
            CurrentSection = Section(value.Key);
        }
    }

    private object Section(string key)
    {
        if (!_created.TryGetValue(key, out var section))
        {
            section = _factories[key]();
            _created[key] = section;
        }

        return section;
    }
}

/// <summary>NET-004: eigene DNS-Server, strikt geprueft; private Adressen nur nach Bestaetigung.</summary>
public sealed partial class CustomDnsViewModel : ObservableObject
{
    private readonly TweakWorkspace _workspace;
    private readonly IDialogService _dialogs;
    private readonly Localizer _localizer;

    [ObservableProperty]
    private string _input = string.Empty;

    [ObservableProperty]
    private string? _error;

    public CustomDnsViewModel(TweakWorkspace workspace, IDialogService dialogs)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _localizer = workspace.Localizer;
    }

    [RelayCommand]
    private async Task Apply()
    {
        var parts = Input.Replace(';', ',').Replace(' ', ',');
        var list = DnsServers.Parse(string.Join(',', parts.Split(',', StringSplitOptions.RemoveEmptyEntries)));
        if (list is not { Count: > 0 and <= 8 } || !list.All(DnsServers.IsUsable))
        {
            Error = _localizer["dns.custom.invalid"];
            return;
        }

        Error = null;
        if (list.Any(DnsServers.IsPrivate) && !await _dialogs.ShowAsync(new ConfirmDialogViewModel(
                _localizer["dns.custom.private.title"], _localizer["dns.custom.private.body"], _localizer["dns.custom.apply"], _localizer["dialog.cancel"], danger: true)).ConfigureAwait(true))
        {
            return;
        }

        await _workspace.ApplyIdsAsync([CustomDnsTweaks.IdFor(list)]).ConfigureAwait(true);
    }
}

public sealed record PingRow(string Target, string Summary);

public sealed partial class NetworkToolsViewModel : ObservableObject
{
    private readonly INetworkTools _tools;
    private readonly IDialogService _dialogs;
    private readonly Localizer _localizer;

    [ObservableProperty]
    private bool _isPinging;

    [ObservableProperty]
    private int _pingProgress;

    [ObservableProperty]
    private string _target = PingTest.DefaultTargets[0];

    [ObservableProperty]
    private string? _dnsText;

    [ObservableProperty]
    private string? _dohText;

    public NetworkToolsViewModel(INetworkTools tools, IDialogService dialogs, Localizer localizer)
    {
        _tools = tools;
        _dialogs = dialogs;
        _localizer = localizer;
    }

    public IReadOnlyList<string> Targets => PingTest.DefaultTargets;

    public ObservableCollection<PingRow> Results { get; } = [];

    public async Task LoadAsync()
    {
        var doh = await Task.Run(_tools.Doh).ConfigureAwait(true);
        DohText = !doh.Supported
            ? _localizer["net.doh.unsupported"]
            : doh.ServersWithDoh.Count > 0
                ? _localizer.Format("net.doh.possible", ("servers", string.Join(", ", doh.ServersWithDoh)))
                : _localizer["net.doh.noserver"];
    }

    [RelayCommand]
    private async Task Ping()
    {
        if (IsPinging)
        {
            return;
        }

        IsPinging = true;
        PingProgress = 0;
        try
        {
            var stats = await _tools.PingAsync(Target.Trim(), new Progress<int>(p => PingProgress = p), CancellationToken.None).ConfigureAwait(true);
            Results.Insert(0, new PingRow(stats.Target, _localizer.Format(
                "net.ping.result",
                ("received", stats.Received),
                ("sent", stats.Sent),
                ("loss", stats.LossPercent.ToString("0.#", CultureInfo.CurrentCulture)),
                ("min", Formatting.Ms(stats.MinMs)),
                ("median", Formatting.Ms(stats.MedianMs)),
                ("max", Formatting.Ms(stats.MaxMs)),
                ("jitter", Formatting.Ms(stats.JitterMs)))));
        }
        catch (AscendedTweaksException)
        {
            Results.Insert(0, new PingRow(Target, _localizer["net.ping.invalid"]));
        }
        finally
        {
            IsPinging = false;
        }
    }

    [RelayCommand]
    private async Task FlushDns()
    {
        var ok = await Task.Run(_tools.FlushDnsCache).ConfigureAwait(true);
        DnsText = _localizer[ok ? "net.flush.ok" : "net.flush.failed"];
    }

    [RelayCommand]
    private void OpenDnsSettings() => _tools.OpenDnsSettings();

    [RelayCommand]
    private async Task NetworkReset()
    {
        if (!await _dialogs.ShowAsync(new ConfirmDialogViewModel(_localizer["net.reset.title"], _localizer["net.reset.body"], _localizer["net.reset.run"], _localizer["dialog.cancel"], danger: true)).ConfigureAwait(true))
        {
            return;
        }

        MaintenanceResult result;
        try
        {
            result = await Task.Run(_tools.NetworkReset).ConfigureAwait(true);
        }
        catch (AscendedTweaksException ex)
        {
            result = new MaintenanceResult(false, false, ex.Message);
        }

        await _dialogs.ShowAsync(new ResultDialogViewModel(_localizer["net.reset.title"], result.Ok ? _localizer["net.reset.done"] : result.Message ?? _localizer["result.failed"], [], result.RebootRequired)).ConfigureAwait(true);
    }
}

public sealed record VolumeRow(VolumeInfo Volume, string Title, string Detail, bool CanReTrim, bool CanDefrag, string? DefragNote);

public sealed record HealthRow(string Title, string Detail);

public sealed partial class DrivesViewModel : ObservableObject
{
    private readonly IDiskTools _tools;
    private readonly IDialogService _dialogs;
    private readonly Localizer _localizer;
    private readonly bool _readOnly;

    [ObservableProperty]
    private bool _isBusy;

    public DrivesViewModel(IDiskTools tools, IDialogService dialogs, Localizer localizer, bool readOnly)
    {
        _tools = tools;
        _dialogs = dialogs;
        _localizer = localizer;
        _readOnly = readOnly;
    }

    public ObservableCollection<VolumeRow> Volumes { get; } = [];

    public ObservableCollection<HealthRow> Health { get; } = [];

    public async Task LoadAsync()
    {
        var volumes = await Task.Run(_tools.Volumes).ConfigureAwait(true);
        Volumes.Clear();
        foreach (var v in volumes)
        {
            var media = _localizer["disk.media." + v.Media];
            Volumes.Add(new VolumeRow(
                v,
                $"{v.Drive}: {v.Model ?? string.Empty}".Trim(),
                _localizer.Format("disk.volume.detail", ("media", media), ("size", CleanupPageViewModel.Size(v.SizeBytes)), ("free", CleanupPageViewModel.Size(v.FreeBytes))),
                v.Media == MediaKind.Ssd && !_readOnly,
                v.Media == MediaKind.Hdd && !_readOnly,
                v.Media == MediaKind.Hdd ? null : _localizer["disk.defrag.locked"]));
        }
    }

    [RelayCommand]
    private Task ReTrim(VolumeRow row) => Run(MaintenanceAction.ReTrim, row);

    [RelayCommand]
    private Task Defrag(VolumeRow row) => Run(MaintenanceAction.Defrag, row);

    [RelayCommand]
    private async Task ReadHealth()
    {
        IsBusy = true;
        try
        {
            var disks = await Task.Run(_tools.Health).ConfigureAwait(true);
            Health.Clear();
            foreach (var d in disks)
            {
                Health.Add(new HealthRow(d.Model, _localizer.Format(
                    "disk.health.detail",
                    ("temp", d.TemperatureCelsius?.ToString(CultureInfo.CurrentCulture) ?? "-"),
                    ("wear", d.WearPercent?.ToString(CultureInfo.CurrentCulture) ?? "-"),
                    ("hours", d.PowerOnHours?.ToString(CultureInfo.CurrentCulture) ?? "-"),
                    ("errors", d.ReadErrors?.ToString(CultureInfo.CurrentCulture) ?? "-"),
                    ("predict", _localizer[d.FailurePredicted switch { true => "disk.health.warning", false => "disk.health.ok", null => "anticheat.value.unknown" }]))));
            }
        }
        catch (AscendedTweaksException ex)
        {
            Health.Clear();
            Health.Add(new HealthRow(_localizer["result.failed"], ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task Run(MaintenanceAction action, VolumeRow row)
    {
        var key = action == MaintenanceAction.ReTrim ? "disk.retrim" : "disk.defrag";
        if (row is null || !await _dialogs.ShowAsync(new ConfirmDialogViewModel(_localizer[key], _localizer.Format(key + ".body", ("drive", row.Volume.Drive.ToString())), _localizer[key], _localizer["dialog.cancel"])).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        MaintenanceResult result;
        try
        {
            result = await Task.Run(() => _tools.Run(action, row.Volume.Drive)).ConfigureAwait(true);
        }
        catch (AscendedTweaksException ex)
        {
            result = new MaintenanceResult(false, false, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await _dialogs.ShowAsync(new ResultDialogViewModel(_localizer[key], result.Ok ? _localizer["disk.done"] : result.Message ?? _localizer["result.failed"], [], false)).ConfigureAwait(true);
    }
}

