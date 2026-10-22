using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AscendedTweaks.App.Services;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Catalog;
using AscendedTweaks.Core;
using AscendedTweaks.Engine;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Engine.State;
using AscendedTweaks.Engine.Tests;
using AscendedTweaks.Ipc;
using AscendedTweaks.Localization;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

/// <summary>Dialoge werden ueber eine Antwortfunktion beantwortet; alle gezeigten Dialoge werden protokolliert.</summary>
internal sealed class ScriptedDialogs : IDialogService
{
    private readonly Func<DialogViewModel, object?> _answer;

    public ScriptedDialogs(Func<DialogViewModel, object?> answer) => _answer = answer;

    public List<DialogViewModel> Shown { get; } = [];

    public Task<T> ShowAsync<T>(DialogViewModel<T> dialog)
    {
        Shown.Add(dialog);
        return Task.FromResult((T)_answer(dialog)!);
    }

    public void Show(DialogViewModel dialog) => Shown.Add(dialog);

    public void Dismiss(DialogViewModel dialog)
    {
    }

    public T? Last<T>()
        where T : DialogViewModel => Shown.OfType<T>().LastOrDefault();
}

internal sealed class SyncInvoker : IUiInvoker
{
    public T Invoke<T>(Func<Task<T>> action) => action().GetAwaiter().GetResult();
}

internal sealed class CountingRestarter : IExplorerRestarter
{
    public int Restarts { get; private set; }

    public void Restart() => Restarts++;
}

internal sealed class DirectHelper : IHelperChannel
{
    private readonly IHelperHandler _handler;

    public DirectHelper(IHelperHandler handler) => _handler = handler;

    public BatchResultDto ApplyTweaks(ApplyTweaksRequest request) => Call<BatchResultDto>(IpcCommands.ApplyTweaks, request);

    public RevertTweakResponse RevertTweak(RevertTweakRequest request) => Call<RevertTweakResponse>(IpcCommands.RevertTweak, request);

    public TweakStatus Capture(string tweakId) => Call<TweakStatus>(IpcCommands.Capture, new TweakIdRequest { Id = tweakId });

    public RestorePointResult CreateRestorePoint(string name) => Call<RestorePointResult>(IpcCommands.CreateRestorePoint, new RestorePointRequest { Name = name });

    public IReadOnlyList<string> RecoverInterrupted(string batchId, TweakScope scope) => Call<List<string>>(IpcCommands.RecoverInterrupted, new RecoverRequest { BatchId = batchId, Scope = scope });

    public CleanupScan ScanCleanup(string categoryId) => Call<CleanupScan>(IpcCommands.ScanCleanup, new CleanupRequest { CategoryId = categoryId });

    public CleanupResult RunCleanup(string categoryId, CleanupMode mode) => Call<CleanupResult>(IpcCommands.RunCleanup, new CleanupRequest { CategoryId = categoryId, Mode = mode });

    public IReadOnlyList<AscendedTweaks.Engine.Debloat.RemovedApp> RemoveApps(IReadOnlyList<string> names, bool deprovision) =>
        Call<List<AscendedTweaks.Engine.Debloat.RemovedApp>>(IpcCommands.RemoveApps, new RemoveAppsRequest { Names = names, Deprovision = deprovision });

    public AscendedTweaks.Engine.Tools.MaintenanceResult RunMaintenance(AscendedTweaks.Engine.Tools.MaintenanceAction action, char? drive) =>
        Call<AscendedTweaks.Engine.Tools.MaintenanceResult>(IpcCommands.RunMaintenance, new MaintenanceRequest { Action = action, Drive = drive });

    public IReadOnlyList<AscendedTweaks.Engine.Tools.DiskHealth> ReadDiskHealth() => Call<List<AscendedTweaks.Engine.Tools.DiskHealth>>(IpcCommands.ReadDiskHealth, new { });

    public void Dispose()
    {
    }

    private T Call<T>(string command, object request)
    {
        var result = _handler.Handle(command, JsonSerializer.SerializeToElement(request, request.GetType(), CoreJson.Options));
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(result, result!.GetType(), CoreJson.Options), CoreJson.Options)!;
    }
}

public sealed class WorkspaceTests : IDisposable
{
    private const string P = TestTweaks.Base;
    private readonly Harness _h = new();
    private readonly CountingRestarter _restarter = new();
    private readonly TweakCatalog _catalog;
    private readonly Localizer _localizer = new(Localizer.German);
    private readonly AppSettings _settings = new();
    private ScriptedDialogs _dialogs;
    private TweakService _service;
    private TweakWorkspace _workspace;

    public WorkspaceTests()
    {
        _h.Registry.SeedKey(RegHive.HKCU, "Software");
        _h.Registry.Seed(RegHive.HKCU, P, "A", RegistryData.DWord(1));
        _h.Registry.Seed(RegHive.HKCU, P, "B", RegistryData.DWord(1));
        _h.Registry.Seed(RegHive.HKCU, P, "C", RegistryData.DWord(0));
        _h.Registry.Seed(RegHive.HKLM, @"SOFTWARE\AscendedTweaksTest", "M", RegistryData.DWord(1));
        TweakDefinition[] tweaks =
        [
            TestTweaks.Dword("PRIV-901", P, "A", 0),
            TestTweaks.Create(
                "PRIV-902",
                new RegistryValueEqualsCondition { Hive = RegHive.HKCU, Path = P, Name = "B", Value = new RegLiteral { Number = 0 } },
                [new RegistrySetValueAction { Hive = RegHive.HKCU, Path = P, Name = "B", Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 0 } }],
                risk: RiskLevel.Expert,
                hidden: true),
            TestTweaks.Dword("PRIV-903", P, "C", 0),
            TestTweaks.CreateKey("UI-901", @"Software\Classes\CLSID\{11111111-2222-3333-4444-555555555555}\InprocServer32") is var ui
                ? TestTweaks.Create("UI-901", ui.Detect, ui.Apply, restartExplorer: true)
                : null!,
            TestTweaks.Dword("PERF-901", @"SOFTWARE\AscendedTweaksTest", "M", 0, RegHive.HKLM),
            TestTweaks.Service("PERF-916", "Fax", ServiceStartSetting.Disabled),
        ];
        _h.Services.Services["Fax"] = new ServiceConfig(ServiceStartType.Manual, false, false);
        var texts = tweaks.SelectMany(t => new[] { t.NameKey, t.DescriptionKey, t.SideEffectsKey })
            .ToDictionary(k => k, k => "Text " + k);
        _catalog = new TweakCatalog(tweaks, new Dictionary<string, TextBundle>
        {
            [Localizer.German] = new(Localizer.German, texts),
            [Localizer.English] = new(Localizer.English, texts),
        });
        _dialogs = Answering();
        _service = CreateService();
        _workspace = CreateWorkspace();
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Toggle_marks_pending_and_discard_clears()
    {
        await _workspace.RefreshAsync();
        var card = _workspace.Card("PRIV-901");

        card.IsOn.Should().BeFalse();
        card.IsOn = true;
        _workspace.PendingCount.Should().Be(1);
        _workspace.PendingText.Should().Be("1 Dinge werden geändert. Neustart nötig: nein.");
        card.IsOn = false;
        _workspace.PendingCount.Should().Be(0, "zurueck auf den Ist-Zustand ist keine Aenderung");

        card.IsOn = true;
        _workspace.DiscardCommand.Execute(null);
        _workspace.HasPending.Should().BeFalse();
        card.IsOn.Should().BeFalse();
    }

    [Fact]
    public async Task Apply_shows_preview_first_then_applies_and_verifies()
    {
        await _workspace.RefreshAsync();
        _workspace.Card("PRIV-901").IsOn = true;

        await _workspace.ApplyCommand.ExecuteAsync(null);

        _dialogs.Shown[0].Should().BeOfType<PreviewDialogViewModel>();
        var preview = (PreviewDialogViewModel)_dialogs.Shown[0];
        preview.Rows.Single().Current.Should().Be("DWord 1 (0x1)");
        preview.Rows.Single().New.Should().Be("DWord 0 (0x0)");
        preview.UserOnly.Should().BeTrue();
        _dialogs.Shown.Should().NotContain(d => d is ConfirmDialogViewModel, "der Hinweis stand in der Vorschau, keine zweite Rueckfrage");
        _dialogs.Last<ResultDialogViewModel>()!.Rows.Should().ContainSingle(r => r.Ok);
        _h.Registry.GetValue(RegHive.HKCU, P, "A").Should().Be(RegistryData.DWord(0));
        _workspace.HasPending.Should().BeFalse();
        _workspace.Card("PRIV-901").State.Should().Be(TweakState.Applied);
        _workspace.Card("PRIV-901").Tracked.Should().BeTrue();
    }

    [Fact]
    public async Task Preview_only_and_declined_preview_change_nothing()
    {
        await _workspace.RefreshAsync();
        _workspace.Card("PRIV-901").IsOn = true;

        await _workspace.PreviewCommand.ExecuteAsync(null);
        ((PreviewDialogViewModel)_dialogs.Shown.Single()).PreviewOnly.Should().BeTrue();

        _dialogs = Answering(preview: false);
        _workspace = CreateWorkspace();
        await _workspace.RefreshAsync();
        _workspace.Card("PRIV-901").IsOn = true;
        await _workspace.ApplyCommand.ExecuteAsync(null);

        _h.Registry.WriteCount.Should().Be(0);
        _workspace.HasPending.Should().BeTrue("abgelehnte Vorschau laesst die Auswahl stehen");
    }

    [Fact]
    public async Task Expert_tweak_needs_the_warning_and_is_hidden_without_expert_mode()
    {
        await _workspace.RefreshAsync();
        var card = _workspace.Card("PRIV-902");
        card.IsVisible.Should().BeFalse();
        _workspace.SetExpertMode(true);
        card.IsVisible.Should().BeTrue();

        _dialogs = Answering(expert: false);
        _workspace = CreateWorkspace();
        await _workspace.RefreshAsync();
        _workspace.Card("PRIV-902").IsOn = true;
        await _workspace.ApplyCommand.ExecuteAsync(null);

        _dialogs.Shown.Should().Contain(d => d is ExpertWarningViewModel);
        _h.Registry.GetValue(RegHive.HKCU, P, "B").Should().Be(RegistryData.DWord(1));
    }

    [Fact]
    public async Task Expert_warning_has_three_stages()
    {
        var warning = new ExpertWarningViewModel(_localizer, ["X"]);

        warning.CanContinue.Should().BeTrue();
        warning.ContinueCommand.Execute(null);
        warning.Stage.Should().Be(2);
        warning.CanContinue.Should().BeFalse();
        warning.Understood = true;
        warning.ContinueCommand.Execute(null);
        warning.Stage.Should().Be(3);
        warning.Typed = "verstanden";
        warning.CanContinue.Should().BeFalse("Gross- und Kleinschreibung zaehlt");
        warning.Typed = "VERSTANDEN";
        warning.ContinueCommand.Execute(null);
        (await warning.Completion).Should().BeTrue();
        new ExpertWarningViewModel(new Localizer(Localizer.English), []).Keyword.Should().Be("UNDERSTOOD");
    }

    [Fact]
    public async Task Toggle_off_reverts_and_asks_on_conflict()
    {
        await _workspace.RefreshAsync();
        _workspace.Card("PRIV-901").IsOn = true;
        await _workspace.ApplyCommand.ExecuteAsync(null);
        _h.Registry.Seed(RegHive.HKCU, P, "A", RegistryData.DWord(9));
        await _workspace.RefreshAsync();

        _workspace.Card("PRIV-901").State.Should().Be(TweakState.NotApplied, "Windows hat den Wert geaendert");
        await _workspace.RevertAsync("PRIV-901");

        _dialogs.Last<ConflictDialogViewModel>()!.Current.Should().Be("DWord 9 (0x9)");
        _h.Registry.GetValue(RegHive.HKCU, P, "A").Should().Be(RegistryData.DWord(1));
        _dialogs.Last<ResultDialogViewModel>()!.Rows.Single().Ok.Should().BeTrue();
    }

    [Fact]
    public async Task Applied_by_windows_is_locked_with_reason()
    {
        await _workspace.RefreshAsync();
        var card = _workspace.Card("PRIV-903");

        card.State.Should().Be(TweakState.Applied);
        card.AppliedByOthers.Should().BeTrue();
        card.IsToggleEnabled.Should().BeFalse();
        card.ToggleToolTip.Should().Contain("nicht durch Ascended Tweaks");
    }

    [Fact]
    public async Task Explorer_restart_only_after_confirmation()
    {
        await _workspace.RefreshAsync();
        _workspace.Card("UI-901").IsOn = true;
        await _workspace.ApplyCommand.ExecuteAsync(null);

        _restarter.Restarts.Should().Be(1);
        _dialogs.Shown.OfType<ConfirmDialogViewModel>().Should().Contain(d => d.Title.Contains("Explorer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Mixed_batch_runs_machine_part_through_helper()
    {
        await _workspace.RefreshAsync();
        _workspace.Card("PRIV-901").IsOn = true;
        _workspace.Card("PERF-901").IsOn = true;

        await _workspace.ApplyCommand.ExecuteAsync(null);

        ((PreviewDialogViewModel)_dialogs.Shown[0]).AdminRequired.Should().BeTrue();
        _h.Registry.GetValue(RegHive.HKLM, @"SOFTWARE\AscendedTweaksTest", "M").Should().Be(RegistryData.DWord(0));
        _h.RestorePoints.Calls.Should().Be(1);
        _dialogs.Last<ResultDialogViewModel>()!.Rows.Should().HaveCount(2).And.OnlyContain(r => r.Ok);
    }

    [Fact]
    public async Task Failed_restore_point_for_machine_tweaks_is_asked_explicitly()
    {
        _h.RestorePoints.Status = RestorePointStatus.ProtectionDisabled;
        _dialogs = new ScriptedDialogs(d => d is ConfirmDialogViewModel ? false : true);
        _workspace = CreateWorkspace();
        await _workspace.RefreshAsync();
        _workspace.Card("PERF-901").IsOn = true;

        await _workspace.ApplyCommand.ExecuteAsync(null);

        _dialogs.Shown.OfType<ConfirmDialogViewModel>().Should().ContainSingle();
        _h.Registry.GetValue(RegHive.HKLM, @"SOFTWARE\AscendedTweaksTest", "M").Should().Be(RegistryData.DWord(1));
    }

    [Fact]
    public async Task Details_show_technical_rows_and_reset_needs_tracking()
    {
        await _workspace.RefreshAsync();
        _workspace.OpenDetails(_workspace.Card("PRIV-901"));

        _workspace.Details!.Technical.Single().Target.Should().Contain("AscendedTweaksTest : A");
        _workspace.Details.CanReset.Should().BeFalse();
        _workspace.Details.Sources.Should().NotBeEmpty();
        _workspace.CloseDetailsCommand.Execute(null);
        _workspace.Details.Should().BeNull();
    }

    [Fact]
    public async Task Restore_page_lists_and_undoes_everything_then_compares_with_baseline()
    {
        await _workspace.RefreshAsync();
        _service.CaptureBaseline().CapturedPlaces.Should().BeGreaterThan(0);
        _workspace.Card("PRIV-901").IsOn = true;
        await _workspace.ApplyCommand.ExecuteAsync(null);
        var page = new RestorePageViewModel(_workspace, _dialogs);

        await page.LoadAsync();
        page.Applied.Should().ContainSingle(a => a.TweakId == "PRIV-901");
        page.Backups.Should().Contain(b => b.IsBaseline && !b.CanDelete);
        page.HasInterrupted.Should().BeFalse();

        await page.UndoAllCommand.ExecuteAsync(null);

        _h.Registry.GetValue(RegHive.HKCU, P, "A").Should().Be(RegistryData.DWord(1));
        _dialogs.Last<ResultDialogViewModel>()!.Summary.Should().Be("Alle erfassten Stellen entsprechen dem Ausgangszustand.");
        page.Applied.Should().BeEmpty();
        page.Backups.Should().Contain(b => b.CanDelete);
        await page.DeleteBackupCommand.ExecuteAsync(page.Backups.First(b => b.CanDelete));
        page.Backups.Should().OnlyContain(b => b.IsBaseline);
    }

    [Fact]
    public async Task First_steps_requires_consent_and_saves_completion()
    {
        var store = new SettingsStore(Path.Combine(_h.StateRoot, "settings.json"));
        var firstSteps = new FirstStepsViewModel(_workspace, _settings, store, new SettingsViewModel(_localizer, new NoTheme(), store, _settings));

        firstSteps.NextCommand.Execute(null);
        firstSteps.Step.Should().Be(2);
        firstSteps.CanContinue.Should().BeFalse();
        firstSteps.NextCommand.Execute(null);
        firstSteps.Step.Should().Be(2);
        firstSteps.Understood = true;
        firstSteps.NextCommand.Execute(null);
        firstSteps.IsLastStep.Should().BeTrue("V2: drei Schritte, Sprache und Design kommen aus Windows");
        firstSteps.StepText.Should().Contain("3");
        await firstSteps.CaptureBaselineCommand.ExecuteAsync(null);
        firstSteps.BaselineStatus.Should().Contain("Stellen gesichert");
        await firstSteps.CaptureBaselineCommand.ExecuteAsync(null);
        firstSteps.BaselineStatus.Should().Be("Der Ausgangszustand ist bereits gesichert.");
        firstSteps.BackCommand.Execute(null);
        firstSteps.Step.Should().Be(2);
        firstSteps.NextCommand.Execute(null);
        firstSteps.NextCommand.Execute(null);

        (await firstSteps.Completion).Should().BeTrue();
        store.Load().FirstStepsCompleted.Should().BeTrue();
    }

    [Fact]
    public void First_steps_switches_language_live_and_saves_it()
    {
        _settings.Language = Localizer.German;
        var store = new SettingsStore(Path.Combine(_h.StateRoot, "settings.json"));
        var firstSteps = new FirstStepsViewModel(_workspace, _settings, store, new SettingsViewModel(_localizer, new NoTheme(), store, _settings));
        var changed = new List<string?>();
        firstSteps.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        firstSteps.StepText.Should().Be("Schritt 1 von 3");
        firstSteps.Languages.Single(l => l.IsActive).Culture.Should().Be(Localizer.German);

        firstSteps.SelectLanguageCommand.Execute(Localizer.English);

        _localizer.Culture.Should().Be(Localizer.English);
        _localizer["firststeps.title"].Should().Be("First steps");
        _localizer["firststeps.next"].Should().Be("Next");
        firstSteps.StepText.Should().Be("Step 1 of 3");
        firstSteps.Languages.Single(l => l.IsActive).Culture.Should().Be(Localizer.English);
        changed.Should().Contain(nameof(FirstStepsViewModel.Languages)).And.Contain(nameof(FirstStepsViewModel.StepText));
        store.Load().Language.Should().Be(Localizer.English);
    }

    [Fact]
    public void Progress_dialog_reports_real_progress_and_cancels()
    {
        var progress = new ProgressDialogViewModel(id => "Name " + id);

        progress.IsIndeterminate.Should().BeTrue();
        progress.Report(new BatchProgress(2, 5, "PRIV-901"));
        progress.CurrentTweak.Should().Be("Name PRIV-901");
        progress.IsIndeterminate.Should().BeFalse();
        progress.RequestCancelCommand.Execute(null);
        progress.Token.IsCancellationRequested.Should().BeTrue();
        progress.IsCancelling.Should().BeTrue();
    }

    [Fact]
    public void All_keys_used_in_app_code_exist()
    {
        var keys = Localizer.Bundle(Localizer.German).Keys.ToHashSet();
        var root = FindRepo();
        var patterns = new[]
        {
            new Regex(@"Loc ([a-z0-9.]+)\}"),
            new Regex(@"_localizer\[""([a-z0-9.]+)""\]"),
            new Regex(@"Localizer(?:\.Current)?\[""([a-z0-9.]+)""\]"),
            new Regex(@"Format\(""([a-z0-9.]+)"""),
            new Regex(@"""((?:nav|restore|risk|evidence|settings|card|result|firststeps)\.[a-z0-9.]+)"""),
        };
        var missing = Directory.EnumerateFiles(Path.Combine(root, "src", "AscendedTweaks.App"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal)) && !f.Contains("\\obj\\", StringComparison.Ordinal))
            .SelectMany(f => patterns.SelectMany(p => p.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)))
            .Where(k => !k.EndsWith(".json", StringComparison.Ordinal) && !k.EndsWith('.') && !keys.Contains(k))
            .Distinct()
            .ToList();

        missing.Should().BeEmpty();

        // Zusammengesetzte Schluessel (Praefix + Enum-Wert) gezielt pruefen.
        var composed = Enum.GetNames<AscendedTweaks.Engine.Debloat.RestoreOutcome>().Select(o => "restore.apps.outcome." + o)
            .Concat(Enum.GetNames<AscendedTweaks.Engine.Debloat.AppGroup>().Select(g => "debloat.group." + g))
            .Concat(Enum.GetNames<AscendedTweaks.Engine.Debloat.OneDriveBlock>().Where(b => b != "NotInstalled" && b != "KnownFolderMove" && b != "OnlineOnlyFiles").Select(b => "onedrive.block." + b))
            .Concat(Enum.GetNames<AscendedTweaks.Engine.Insights.AutostartSource>().Select(s => "autostart.source." + s))
            .Concat(Enum.GetNames<AscendedTweaks.Engine.Insights.AutostartRating>().Select(r => "autostart.rating." + r))
            .Concat(Enum.GetNames<AscendedTweaks.App.Services.LaunchOutcome>().Select(o => "gaming.start." + o))
            .Concat(CleanupCategories.All.SelectMany(c => new[] { $"clean.{c.Id}.name", $"clean.{c.Id}.desc", $"clean.{c.Id}.side" }))
            .Concat(AscendedTweaks.Engine.Debloat.DebloatCatalog.Apps.SelectMany(a => new[] { $"deb.{a.Id}.name", $"deb.{a.Id}.desc" }))
            .Concat(Enum.GetNames<AscendedTweaks.Engine.Tools.MediaKind>().Select(m => "disk.media." + m))
            .Concat(["disk.retrim.body", "disk.defrag.body", "disk.health.warning", "disk.health.ok"])
            .Concat(Enum.GetNames<TweakState>().Select(t => "profiles.tweakstate." + t))
            .Concat(AscendedTweaks.Catalog.ProfileLoader.BuiltInKeys.SelectMany(k => new[] { $"preset.{k}.name", $"preset.{k}.desc" }))
            .Concat(Enum.GetNames<PresetLevel>().Select(l => $"level.{l.ToLowerInvariant()}.count"))
            .Concat(KnownStartupApps.All.SelectMany(a => new[] { $"autostart.known.{a.Id}", $"autostart.known.{a.Id}.short" }))
            .Concat(KnownProcesses.Windows.Values.Distinct().Select(k => "process.known." + k))
            .Concat(["process.unknown"])
            .Concat(new[] { "installer", "dump", "cache", "oldvideo", "oldarchive", "duplicate", "recent", "library", "project", "keeptype", "cloud", "unknown", "game", "system" }.Select(w => "large.why." + w))
            .Concat(new[] { "protected", "missing", "changed", "inuse", "failed" }.Select(k => "large.skip." + k))
            .Concat(new[] { "refresh", "power", "hybrid", "ramspeed", "ramcount", "disk", "gamemode", "driver", "background", "anticheat" }.Select(c => $"check.{c}.title"))
            .Concat(Enum.GetNames<CheckState>().Select(s => "check.state." + s))
            .Concat(new[] { "refresh.low", "refresh.ok", "refresh.howto", "power.saver", "power.ok", "power.howto", "hybrid.yes", "hybrid.no", "hybrid.howto", "ramspeed.low", "ramspeed.ok", "ramspeed.howto", "ramcount.one", "ramcount.ok", "ramcount.howto", "disk.low", "disk.ok", "disk.howto", "gamemode.on", "gamemode.off", "gamemode.howto", "driver.old", "driver.ok", "driver.howto", "background.count", "background.top", "anticheat.attention", "anticheat.ok", "anticheat.howto", "unknown", "link.settings", "link.vendor" }.Select(k => "check." + k))
            .Concat(new[] { "faster", "background", "privacy", "games", "updates", "network", "storage", "look", "cleanup" }.Select(g => "group." + g))
            .Concat(new[] { "Performance.Services", "Privacy.X", "Gaming.X", "Updates.X", "Network.X", "Disk.X", "UI.X", "Performance.X" }.Select(c => "group." + PresetGroups.KeyFor(c)))
            .Concat(Enum.GetNames<AscendedTweaks.Engine.Insights.DiskKind>().Select(k => "dashboard.disk." + k))
            .Concat(new[] { "safe", "caution", "expert" }.Select(r => "risk.plain." + r))
            .Concat(new[] { "noticeable", "small", "unclear" }.Select(i => "impact." + i))
            .Concat(new[] { "autostart", "dns", "cache", "registry", "service", "backup", "restorepoint", "admin" }.SelectMany(g => new[] { $"glossary.{g}.term", $"glossary.{g}.text" }))
            .Concat(new[] { "simple.start.help", "simple.faster.help", "simple.gaming.help", "simple.privacy.help", "simple.back.help" })
            .Concat(new[] { "autostart", "free", "ramload", "reboot", "trim", "secureboot", "tpm", "hvci", "power", "gamemode", "defender" }.SelectMany(k => new[] { $"dashboard.ind.{k}", $"dashboard.ind.{k}.why" }));
        composed.Where(k => !keys.Contains(k)).Should().BeEmpty();
    }

    [Fact]
    public async Task Autostart_entry_is_disabled_after_preview_and_enabled_again()
    {
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        _h.Registry.Seed(RegHive.HKCU, run, "Steam", RegistryData.String(@"C:\Steam\steam.exe"));
        var before = _h.Registry.Snapshot();
        var inventory = new AutostartInventory(_h.Registry, new NoSources(), new AlwaysSigned("Valve Corp."));
        var vm = new AutostartViewModel(inventory.Read, _workspace);

        await vm.LoadAsync();
        var row = vm.Rows.Single(r => r.Entry.Name == "Steam");
        row.CanDisable.Should().BeTrue();
        row.CanEnable.Should().BeFalse();

        await vm.DisableCommand.ExecuteAsync(row);

        _dialogs.Last<PreviewDialogViewModel>().Should().NotBeNull("auch Autostart-Aenderungen zeigen zuerst die Vorschau");
        AutostartInventory.IsApproved(_h.Registry.GetValue(RegHive.HKCU, approved, "Steam")).Should().BeFalse();
        row = vm.Rows.Single(r => r.Entry.Name == "Steam");
        row.StateText.Should().Be("Deaktiviert");
        row.CanEnable.Should().BeTrue();
        _workspace.NameOf(row.Entry.TweakId!).Should().Be("Autostart „Steam“ deaktivieren");

        var restore = new RestorePageViewModel(_workspace, _dialogs);
        await restore.LoadAsync();
        restore.Applied.Should().ContainSingle(a => a.TweakId == "PERF-026/HKCU-RUN/Steam" && a.Scope == TweakScope.User);

        await vm.EnableCommand.ExecuteAsync(row);
        _h.Registry.Snapshot().Should().Be(before);
        vm.Rows.Single(r => r.Entry.Name == "Steam").CanDisable.Should().BeTrue();
    }

    [Fact]
    public async Task Known_autostart_program_gets_plain_text_and_memory_only_while_running()
    {
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        _h.Registry.Seed(RegHive.HKCU, run, "Steam", RegistryData.String(@"C:\Steam\steam.exe -silent"));
        var inventory = new AutostartInventory(_h.Registry, new NoSources(), new AlwaysSigned("Valve Corp."));
        var processes = new ListProcesses();
        var vm = new AutostartViewModel(inventory.Read, _workspace, processes);

        await vm.LoadAsync();
        var row = vm.Rows.Single(r => r.Entry.Name == "Steam");
        row.Reason.Should().StartWith("Steam: Spiele-Launcher");
        row.Memory.Should().BeNull("Steam laeuft gerade nicht, also keine Zahl");

        processes.Items.Add(new ProcessSample(1, "steam", null, 300L * 1024 * 1024));
        processes.Items.Add(new ProcessSample(2, "steamwebhelper", null, 10));
        await vm.LoadAsync();
        vm.Rows.Single(r => r.Entry.Name == "Steam").Memory.Should().Be("Braucht gerade 300 MB.");
    }

    [Fact]
    public async Task Background_report_explains_processes_and_jumps_to_the_autostart_section()
    {
        var sampler = new ListProcesses();
        sampler.Items.AddRange([new ProcessSample(1, "svchost", null, 50), new ProcessSample(2, "Discord", null, 40), new ProcessSample(3, "geheim", null, 30)]);
        var page = new PerformancePageViewModel(
            _workspace,
            () => new AutostartViewModel(() => [], _workspace),
            () => new CatalogServicesViewModel(_workspace, _h.Services),
            () => new CatalogTasksViewModel(_workspace, _h.Tasks),
            () => new ProcessesViewModel(new ProcessOverview(sampler, _ => Task.CompletedTask), _localizer));
        page.SelectedSection = page.Sections.Single(s => s.Key == "processes");
        var report = page.CurrentSection.Should().BeOfType<ProcessesViewModel>().Subject;
        await report.MeasureAsync();

        report.ByMemory.Select(r => r.Explanation).Should().Equal("Sammelprozess für Windows-Dienste. Normal, dass es viele gibt.", "Discord (Chat)", "Unbekannt, nicht anfassen.");
        report.ByMemory.Select(r => r.CanJump).Should().Equal(false, true, false);
        report.JumpToAutostartCommand.Execute(null);
        page.SelectedSection.Key.Should().Be("autostart");
    }

    [Fact]
    public async Task Machine_autostart_entry_goes_through_the_helper()
    {
        _h.Registry.Seed(RegHive.HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Tool", RegistryData.String(@"C:\Tool\tool.exe"));
        const string id = "PERF-026/HKLM-RUN/Tool";

        await _workspace.ApplyIdsAsync([id]);

        _service.HelperStarted.Should().BeTrue();
        AutostartInventory.IsApproved(_h.Registry.GetValue(RegHive.HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "Tool")).Should().BeFalse();
        _service.GetStatus(id).TrackedInLedger.Should().BeTrue();
    }

    [Fact]
    public async Task Catalog_service_and_task_overviews_show_default_current_and_target()
    {
        var services = new CatalogServicesViewModel(_workspace, _h.Services);
        await services.LoadAsync();

        var row = services.Rows.Single();
        row.Service.Should().Be("Fax");
        row.CurrentText.Should().Be("jetzt: Manuell");
        row.TargetText.Should().Be("mit Tweak: Deaktiviert");
        row.DefaultText.Should().Be("Windows-Standard: unbekannt");
        services.DetailsCommand.Execute(row);
        _workspace.Details.Should().NotBeNull();

        var tasks = new CatalogTasksViewModel(_workspace, _h.Tasks);
        await tasks.LoadAsync();
        tasks.Rows.Should().BeEmpty("der Testkatalog enthaelt keine Aufgaben");
    }

    [Fact]
    public async Task Performance_page_creates_sections_lazily()
    {
        var created = 0;
        var page = new PerformancePageViewModel(
            _workspace,
            () => { created++; return new AutostartViewModel(() => [], _workspace); },
            () => new CatalogServicesViewModel(_workspace, _h.Services),
            () => new CatalogTasksViewModel(_workspace, _h.Tasks),
            () => new ProcessesViewModel(new ProcessOverview(new NoProcesses(), _ => Task.CompletedTask), _localizer));

        page.CurrentSection.Should().BeOfType<TweakListPageViewModel>();
        created.Should().Be(0);
        page.SelectedSection = page.Sections[1];
        page.SelectedSection = page.Sections[0];
        page.SelectedSection = page.Sections[1];
        created.Should().Be(1);
        page.CurrentSection.Should().BeOfType<AutostartViewModel>();
        page.SelectedSection = page.Sections[4];
        var processes = page.CurrentSection.Should().BeOfType<ProcessesViewModel>().Subject;
        await processes.MeasureAsync();
        processes.TotalText.Should().Be("Von allen Prozessen belegt: 0 MB");
    }

    [Fact]
    public async Task Game_profile_applies_and_reverts_per_game_settings_through_preview()
    {
        const string exe = @"C:\Games\Spiel\spiel.exe";
        var store = new GameProfileStore(Path.Combine(_h.StateRoot, "games.json"));
        var before = _h.Registry.Snapshot();
        var vm = Profiles(store, [new DetectedGame("Spiel", GameSource.Epic, @"C:\Games\Spiel", exe, "x://launch", AntiCheat.None)]);

        await vm.LoadAsync();
        vm.Detected.Should().ContainSingle();
        await vm.AddCommand.ExecuteAsync(vm.Detected[0]);
        var profile = vm.Profiles.Single();
        profile.GpuHighPerformance.Should().BeTrue("neue Profile bevorzugen die leistungsstarke GPU");
        profile.DisableFullscreenOptimizations = true;
        profile.IsApplied.Should().BeFalse();

        await profile.ApplyCommand.ExecuteAsync(null);
        _dialogs.Last<PreviewDialogViewModel>().Should().NotBeNull();
        _h.Registry.GetValue(RegHive.HKCU, GameTweaks.GpuPreferencesPath, exe)!.Text.Should().Be("GpuPreference=2;");
        _h.Registry.GetValue(RegHive.HKCU, GameTweaks.LayersPath, exe)!.Text.Should().Be("~ DISABLEDXMAXIMIZEDWINDOWEDMODE");
        profile = vm.Profiles.Single();
        profile.IsApplied.Should().BeTrue();

        // Abwaehlen und Uebernehmen setzt genau diese Einstellung zurueck.
        profile.DisableFullscreenOptimizations = false;
        await profile.ApplyCommand.ExecuteAsync(null);
        _h.Registry.GetValue(RegHive.HKCU, GameTweaks.LayersPath, exe).Should().BeNull();

        // Entfernen setzt die restlichen Einstellungen zurueck, bevor das Profil verschwindet.
        await vm.Profiles.Single().RemoveCommand.ExecuteAsync(null);
        vm.Profiles.Should().BeEmpty();
        store.Load().Should().BeEmpty();
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public async Task Fortnite_becomes_the_first_profile_and_permanent_priority_is_locked_by_anti_cheat()
    {
        const string exe = @"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe";
        var store = new GameProfileStore(Path.Combine(_h.StateRoot, "games.json"));
        var vm = Profiles(store, [new DetectedGame("Fortnite", GameSource.Epic, @"C:\Program Files\Epic Games\Fortnite", exe, "x://fortnite", AntiCheat.EasyAntiCheat)]);

        await vm.LoadAsync();

        var profile = vm.Profiles.Single();
        profile.Name.Should().Be("Fortnite");
        profile.CanUsePermanentPriority.Should().BeFalse();
        profile.PermanentPriority = true;
        profile.PermanentPriority.Should().BeFalse("GAME-016 ist bei Anti-Cheat-Spielen gesperrt");
        profile.Profile.DesiredTweakIds().Should().NotContain(id => id.StartsWith("GAME-016", StringComparison.Ordinal));
        vm.Detected.Should().BeEmpty("das Spiel hat bereits ein Profil");
    }

    [Fact]
    public async Task Start_reports_the_launcher_result()
    {
        var launcher = new FakeLauncher(new LaunchResult(LaunchOutcome.PriorityDenied, "Zugriff verweigert"));
        var store = new GameProfileStore(Path.Combine(_h.StateRoot, "games.json"));
        store.Save([new GameProfile { Name = "Spiel", ExePath = @"C:\Games\spiel.exe", StartHighPriority = true }]);
        var vm = new GameProfilesViewModel(() => [], store, _workspace, launcher, new NoPicker());
        await vm.LoadAsync();

        await vm.Profiles.Single().StartCommand.ExecuteAsync(null);

        launcher.Started.Single().StartHighPriority.Should().BeTrue();
        vm.Profiles.Single().StatusText.Should().Contain("nicht umgangen").And.Contain("Zugriff verweigert");
    }

    [Fact]
    public async Task Anti_cheat_view_shows_states_without_changing_anything()
    {
        var writes = _h.TotalWrites;
        var vm = new AntiCheatViewModel(new FixedFacts(new SecurityFacts(false, 2, true, true, true, null)), _localizer);

        await vm.LoadAsync();

        vm.Rows.Select(r => r.StateText).Should().Equal("Aus", "TPM 2.0", "An", "An", "An", "Nicht lesbar");
        vm.Rows[0].Tone.Should().Be(AscendedTweaks.App.Controls.BadgeTone.Warning);
        vm.Rows[4].Tone.Should().Be(AscendedTweaks.App.Controls.BadgeTone.Warning, "Testsignierung an ist ein Anti-Cheat-Problem");
        _h.TotalWrites.Should().Be(writes);
    }

    private GameProfilesViewModel Profiles(GameProfileStore store, IReadOnlyList<DetectedGame> games) =>
        new(() => games, store, _workspace, new FakeLauncher(new LaunchResult(LaunchOutcome.Started)), new NoPicker());

    private sealed class FakeLauncher(LaunchResult result) : IGameLauncher
    {
        public List<GameProfile> Started { get; } = [];

        public Task<LaunchResult> StartAsync(GameProfile profile, CancellationToken cancellation)
        {
            Started.Add(profile);
            return Task.FromResult(result);
        }
    }

    private sealed class NoPicker : IFilePicker
    {
        public string? PickExe(string? initialFolder) => null;
    }

    private sealed class FixedFacts(SecurityFacts facts) : ISecurityFactsSource
    {
        public SecurityFacts Read() => facts;
    }

    private sealed class NoSources : IAutostartSources
    {
        public IReadOnlyList<StartupFile> StartupFolderFiles() => [];

        public IReadOnlyList<LogonTask> LogonTasks() => [];
    }

    private sealed class AlwaysSigned(string publisher) : ISignatureVerifier
    {
        public SignatureInfo Verify(string filePath) => new(SignatureStatus.Valid, publisher);
    }

    private sealed class NoProcesses : IProcessSampler
    {
        public IReadOnlyList<ProcessSample> Sample() => [];
    }

    private sealed class ListProcesses : IProcessSampler
    {
        public List<ProcessSample> Items { get; } = [];

        public IReadOnlyList<ProcessSample> Sample() => Items.ToList();
    }

    private static string FindRepo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AscendedTweaks.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException();
    }

    private static ScriptedDialogs Answering(bool preview = true, bool expert = true)
    {
        return new ScriptedDialogs(dialog => dialog switch
        {
            PreviewDialogViewModel => preview,
            ExpertWarningViewModel => expert,
            ConflictDialogViewModel => ConflictDecision.RestoreOriginal,
            _ => true,
        });
    }

    private TweakService CreateService()
    {
        var machineRoot = Path.Combine(_h.StateRoot, "Machine");
        var catalog = new CompositeCatalog(_catalog);
        TweakEngine Machine(IRestorePointService rp, bool elevated) =>
            new(catalog, _h.Executors, new StateStore(machineRoot, new FakeTime()), new FakeEnvironment { IsElevated = elevated }, rp, new FakeTime());
        var handler = new EngineHelperHandler(catalog, (rp, _) => Machine(rp, true), _h.RestorePoints, elevated: true);
        return new TweakService(
            catalog,
            new TweakEngine(catalog, _h.Executors, new StateStore(Path.Combine(_h.StateRoot, "User"), new FakeTime()), _h.Environment, new NoRestorePointService(), new FakeTime()),
            Machine(new NoRestorePointService(), false),
            Machine(new NoRestorePointService(), false),
            () => new DirectHelper(handler),
            new FakeTime());
    }

    private TweakWorkspace CreateWorkspace()
    {
        return new TweakWorkspace(_catalog, _service, _localizer, _settings, _dialogs, new SyncInvoker(), _restarter, readOnly: false);
    }

    private sealed class NoTheme : IThemeService
    {
        public ThemeChoice Current => ThemeChoice.Dark;

        public void Apply(ThemeChoice choice)
        {
        }
    }
}
