using AscendedTweaks.App.Services;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Update;
using AscendedTweaks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AscendedTweaks.App.ViewModels;

/// <summary>Protokolle des Benutzers (Abschnitt 10.1): nur lokal, Ordner oeffnen und loeschen.</summary>
public interface ILogFolder
{
    (int Files, long Bytes) Measure();

    /// <summary>Loescht die Protokolle des Benutzers; liefert die Zahl der Dateien, die nicht geloescht werden konnten.</summary>
    int Delete();

    void Open();
}

/// <summary>Update auf Knopfdruck (Abschnitt 12.3).</summary>
public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync();

    Task<UpdateDownload> DownloadAsync(UpdateManifest manifest);

    void StartInstaller(string path);
}

/// <summary>Wartung in den Einstellungen (Abschnitt 6.11): Protokolle, Software-Update, Kommandozeile, App zuruecksetzen.</summary>
public sealed partial class SettingsMaintenanceViewModel : ObservableObject
{
    private readonly ILogFolder _logs;
    private readonly IUpdateService _updates;
    private readonly IDialogService _dialogs;
    private readonly Localizer _localizer;
    private readonly Action _resetSettings;

    [ObservableProperty]
    private string? _logText;

    [ObservableProperty]
    private string? _updateText;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private UpdateManifest? _available;

    public SettingsMaintenanceViewModel(ILogFolder logs, IUpdateService updates, IDialogService dialogs, Localizer localizer, Action resetSettings)
    {
        _logs = logs;
        _updates = updates;
        _dialogs = dialogs;
        _localizer = localizer;
        _resetSettings = resetSettings;
        RefreshLogs();
    }

    public bool HasUpdate => Available is not null;

    /// <summary>Glossar (V2, Abschnitt 2.3): Begriff und Erklaerung in hoechstens 3 Saetzen.</summary>
    public IReadOnlyList<string> GlossaryKeys { get; } = ["autostart", "dns", "cache", "registry", "service", "backup", "restorepoint", "admin"];

    /// <summary>Angezeigte Version (Bereich "Über").</summary>
    public string Version { get; init; } = "0.0.0";

    /// <summary>Oeffnet eine mitgelieferte Textdatei (Quellen, Drittlizenzen, Datenschutz) im Editor.</summary>
    public Action<string>? OpenDocument { get; init; }

    public string VersionText => _localizer.Format("about.version", ("version", Version));

    public string CliText => _localizer.Format("settings.cli.body", ("exe", ProductInfo.CliExecutable + ".exe"));

    public void RefreshLogs()
    {
        var (files, bytes) = _logs.Measure();
        LogText = _localizer.Format("settings.logs.size", ("n", files), ("size", CleanupPageViewModel.Size(bytes)));
    }

    [RelayCommand]
    private void OpenLogs() => _logs.Open();

    [RelayCommand]
    private void OpenSources() => OpenDocument?.Invoke("SOURCES.md");

    [RelayCommand]
    private void OpenNotices() => OpenDocument?.Invoke("THIRD-PARTY-NOTICES.md");

    [RelayCommand]
    private void OpenPrivacy() => OpenDocument?.Invoke("PRIVACY.md");

    [RelayCommand]
    private async Task DeleteLogs()
    {
        if (!await _dialogs.ShowAsync(new ConfirmDialogViewModel(_localizer["settings.logs.delete"], _localizer["settings.logs.delete.body"], _localizer["settings.logs.delete"], _localizer["dialog.cancel"], danger: true)).ConfigureAwait(true))
        {
            return;
        }

        var failed = await Task.Run(_logs.Delete).ConfigureAwait(true);
        RefreshLogs();
        if (failed > 0)
        {
            LogText += " " + _localizer.Format("settings.logs.inuse", ("n", failed));
        }
    }

    [RelayCommand]
    private async Task CheckUpdate()
    {
        IsChecking = true;
        Available = null;
        try
        {
            var result = await _updates.CheckAsync().ConfigureAwait(true);
            UpdateText = result.Outcome switch
            {
                UpdateCheckOutcome.NotConfigured => _localizer["settings.update.notconfigured"],
                UpdateCheckOutcome.UpToDate => _localizer["settings.update.uptodate"],
                UpdateCheckOutcome.Available => _localizer.Format("settings.update.available", ("version", result.Manifest!.Version)),
                _ => _localizer["settings.update.failed"] + " " + result.Error,
            };
            Available = result.Outcome == UpdateCheckOutcome.Available ? result.Manifest : null;
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>Herunterladen und pruefen; gestartet wird erst nach einer zweiten Bestaetigung.</summary>
    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (Available is not { } manifest)
        {
            return;
        }

        IsChecking = true;
        UpdateDownload download;
        try
        {
            download = await _updates.DownloadAsync(manifest).ConfigureAwait(true);
        }
        finally
        {
            IsChecking = false;
        }

        if (!download.Ok)
        {
            UpdateText = _localizer["settings.update.failed"] + " " + download.Error;
            Available = null;
            return;
        }

        var body = _localizer.Format("settings.update.ready.body", ("version", manifest.Version)) + (manifest.Notes is { Length: > 0 } notes ? Environment.NewLine + Environment.NewLine + notes : string.Empty);
        if (await _dialogs.ShowAsync(new ConfirmDialogViewModel(_localizer["settings.update.ready"], body, _localizer["settings.update.start"], _localizer["dialog.later"])).ConfigureAwait(true))
        {
            _updates.StartInstaller(download.InstallerPath!);
        }
    }

    [RelayCommand]
    private async Task ResetSettings()
    {
        if (await _dialogs.ShowAsync(new ConfirmDialogViewModel(_localizer["settings.reset"], _localizer["settings.reset.body"], _localizer["settings.reset"], _localizer["dialog.cancel"])).ConfigureAwait(true))
        {
            _resetSettings();
        }
    }
}
