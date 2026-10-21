using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweakPilot.Engine.Insights;
using TweakPilot.Localization;

namespace TweakPilot.App.ViewModels;

public sealed record PermissionAppRow(string Name, string Detail, string State, bool IsAllowed);

public sealed record PermissionGroupRow(string Title, string Summary, bool IsBlocked, IReadOnlyList<PermissionAppRow> Apps);

/// <summary>
/// Berechtigungs-Uebersicht (Abschnitt 6.4): zeigt pro Faehigkeit, welche Apps Zugriff haben. Nur Anzeige;
/// Aenderungen erfolgen in den Windows-Einstellungen, zu denen ein Knopf fuehrt.
/// </summary>
public sealed partial class PermissionOverviewViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<PermissionCapability>> _read;
    private readonly Localizer _localizer;
    private readonly Action<string> _openUri;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    public PermissionOverviewViewModel(Func<IReadOnlyList<PermissionCapability>> read, Localizer localizer, Action<string> openUri)
    {
        _read = read;
        _localizer = localizer;
        _openUri = openUri;
    }

    public ObservableCollection<PermissionGroupRow> Groups { get; } = [];

    public async Task LoadAsync()
    {
        IsLoading = true;
        Error = null;
        try
        {
            var capabilities = await Task.Run(_read).ConfigureAwait(true);
            var culture = CultureInfo.GetCultureInfo(_localizer.Culture);
            Groups.Clear();
            foreach (var capability in capabilities)
            {
                Groups.Add(ToRow(capability, culture));
            }
        }
#pragma warning disable CA1031 // Eine reine Anzeige darf die Seite nie zum Absturz bringen.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Error = _localizer.Format("perm.error", ("error", ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenSettings() => _openUri("ms-settings:privacy");

    [RelayCommand]
    private Task Reload() => LoadAsync();

    private PermissionGroupRow ToRow(PermissionCapability capability, CultureInfo culture)
    {
        var blocked = capability.Device == ConsentState.Deny || capability.User == ConsentState.Deny;
        var summary = capability.Device == ConsentState.Deny
            ? _localizer["perm.summary.device"]
            : capability.User == ConsentState.Deny
                ? _localizer["perm.summary.user"]
                : _localizer.Format("perm.summary.count", ("n", capability.Apps.Count(a => Effective(capability, a) == ConsentState.Allow)));

        var apps = capability.Apps.Select(a =>
        {
            var effective = blocked ? ConsentState.Deny : Effective(capability, a);
            var kind = _localizer[a.IsDesktopApp ? "perm.kind.desktop" : "perm.kind.store"];
            var used = a.LastUsed is { } t ? _localizer.Format("perm.lastused", ("when", t.ToLocalTime().ToString("g", culture))) : _localizer["perm.neverused"];
            var detail = a.Path is null ? $"{kind} · {used}" : $"{kind} · {used} · {a.Path}";
            return new PermissionAppRow(a.Name, detail, _localizer[StateKey(effective)], effective == ConsentState.Allow);
        }).ToList();

        return new PermissionGroupRow(_localizer["perm.cap." + capability.Capability], summary, blocked, apps);
    }

    /// <summary>Desktop-Apps ohne eigenen Eintrag folgen dem Schalter "Desktop-Apps Zugriff erlauben".</summary>
    private static ConsentState Effective(PermissionCapability capability, PermissionApp app)
    {
        if (app.State != ConsentState.Inherited)
        {
            return app.State;
        }

        return app.IsDesktopApp && capability.DesktopApps != ConsentState.Inherited ? capability.DesktopApps : capability.User;
    }

    private static string StateKey(ConsentState state) => state switch
    {
        ConsentState.Allow => "perm.state.allow",
        ConsentState.Deny => "perm.state.deny",
        ConsentState.Prompt => "perm.state.prompt",
        _ => "perm.state.inherited",
    };
}
