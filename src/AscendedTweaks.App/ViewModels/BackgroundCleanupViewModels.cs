using System.Collections.ObjectModel;
using AscendedTweaks.App.Services;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AscendedTweaks.App.ViewModels;

/// <summary>One program in "Clean up background", with its own close button.</summary>
public sealed partial class BackgroundProgramRow : ObservableObject
{
    public BackgroundProgramRow(BackgroundProgram program, string detail, string closeText)
    {
        Program = program;
        Detail = detail;
        CloseText = closeText;
    }

    public BackgroundProgram Program { get; }

    public string Name => Program.DisplayName;

    public string Detail { get; }

    /// <summary>"Close" for programs with a window; "Force close" (with a question first) for programs without one.</summary>
    public string CloseText { get; }

    [ObservableProperty]
    private bool _isClosing;
}

/// <summary>
/// "Clean up background": programs of the user's session, biggest first, each with a close button. Nothing is closed
/// automatically and nothing is preselected; Windows, antivirus and VPN, helpers of other apps and this app are protected.
/// </summary>
public sealed partial class BackgroundCleanupViewModel : ObservableObject
{
    /// <summary>How long a program gets to close cleanly before the app offers to force it.</summary>
    public static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(5);

    private readonly IRunningPrograms _programs;
    private readonly ProgramCloser _closer;
    private readonly IDialogService _dialogs;
    private readonly Localizer _localizer;
    private readonly string _windowsDirectory;
    private readonly int _selfId;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _summary;

    [ObservableProperty]
    private string? _protectedText;

    [ObservableProperty]
    private string? _error;

    public BackgroundCleanupViewModel(IRunningPrograms programs, IDialogService dialogs, Localizer localizer, string windowsDirectory, int selfId, Func<TimeSpan, Task>? delay = null)
    {
        _programs = programs;
        _closer = new ProgramCloser(programs, delay);
        _dialogs = dialogs;
        _localizer = localizer;
        _windowsDirectory = windowsDirectory;
        _selfId = selfId;
    }

    public ObservableCollection<BackgroundProgramRow> Rows { get; } = [];

    public bool IsEmpty => !IsLoading && Error is null && Rows.Count == 0;

    public async Task LoadAsync()
    {
        IsLoading = true;
        Error = null;
        try
        {
            var groups = await Task.Run(() => BackgroundCleanup.Group(_programs.List(), _windowsDirectory, _selfId)).ConfigureAwait(true);
            Rows.Clear();
            foreach (var program in groups.Where(g => g.Protection == ProgramProtection.None))
            {
                Rows.Add(new BackgroundProgramRow(
                    program,
                    _localizer.Format(program.Ids.Count == 1 ? "bg.row.one" : "bg.row", ("n", program.Ids.Count), ("memory", CleanupPageViewModel.Size(program.WorkingSetBytes))),
                    _localizer[program.HasWindow ? "bg.close" : "bg.force"]));
            }

            Summary = _localizer.Format(
                "bg.summary",
                ("programs", groups.Count),
                ("processes", groups.Sum(g => g.Ids.Count)),
                ("memory", CleanupPageViewModel.Size(groups.Sum(g => g.WorkingSetBytes))));
            ProtectedText = _localizer.Format("bg.protected", ("n", groups.Count(g => g.Protection != ProgramProtection.None)));
        }
#pragma warning disable CA1031 // Read only: a read error must not end the page.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Error = _localizer.Format("perm.error", ("error", ex.Message));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private Task Reload() => LoadAsync();

    [RelayCommand]
    private async Task Close(BackgroundProgramRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.IsClosing)
        {
            return;
        }

        row.IsClosing = true;
        try
        {
            // A program without a window cannot be asked to close; ask the user before forcing it.
            var force = !row.Program.HasWindow;
            if (force && !await ConfirmForceAsync(row).ConfigureAwait(true))
            {
                return;
            }

            var remaining = await _closer.CloseAsync(row.Program, force, CloseWait).ConfigureAwait(true);
            if (remaining > 0 && !force && await ConfirmForceAsync(row).ConfigureAwait(true))
            {
                await _closer.CloseAsync(row.Program, force: true, CloseWait).ConfigureAwait(true);
            }
        }
        finally
        {
            row.IsClosing = false;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private Task<bool> ConfirmForceAsync(BackgroundProgramRow row) =>
        _dialogs.ShowAsync(new ConfirmDialogViewModel(
            _localizer.Format("bg.force.title", ("name", row.Name)),
            _localizer.Format(row.Program.HasWindow ? "bg.force.body.running" : "bg.force.body.nowindow", ("name", row.Name)),
            _localizer["bg.force"],
            _localizer["dialog.cancel"],
            danger: true));
}
