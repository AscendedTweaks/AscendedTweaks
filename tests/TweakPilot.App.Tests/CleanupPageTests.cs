using FluentAssertions;
using TweakPilot.App.Services;
using TweakPilot.App.ViewModels;
using TweakPilot.Core;
using TweakPilot.Localization;
using Xunit;

namespace TweakPilot.App.Tests;

public sealed class CleanupPageTests
{
    private readonly FakeCleanup _service = new();
    private readonly ScriptedDialogs _dialogs = new(_ => true);

    [Fact]
    public async Task Nothing_can_be_cleaned_before_a_scan()
    {
        var page = Page();

        page.CanClean.Should().BeFalse();
        await page.CleanCommand.ExecuteAsync(null);

        _service.Runs.Should().BeEmpty();
        _dialogs.Shown.Should().BeEmpty();
    }

    [Fact]
    public async Task Scan_preselects_only_safe_categories_and_never_the_recycle_bin()
    {
        var page = Page();

        await page.ScanAsync();

        Row(page, "CLEAN-001").IsSelected.Should().BeTrue();
        Row(page, "CLEAN-006").IsSelected.Should().BeFalse("den Papierkorb leeren ist nie Standard");
        Row(page, "CLEAN-009").IsSelected.Should().BeFalse("Vorsicht ist nie vorausgewaehlt");
        Row(page, "CLEAN-010").CanSelect.Should().BeFalse("Browser offen");
        Row(page, "CLEAN-010").Note.Should().Contain("chrome");
        Row(page, "CLEAN-002").IsSelected.Should().BeFalse("nichts zu tun");
        page.CanClean.Should().BeTrue();
    }

    [Fact]
    public async Task Clean_asks_with_clear_warning_and_uses_recycle_bin_only_where_possible()
    {
        var page = Page();
        await page.ScanAsync();
        foreach (var row in page.Rows)
        {
            row.IsSelected = false;
        }

        Row(page, "CLEAN-001").IsSelected = true;
        Row(page, "CLEAN-003").IsSelected = true;

        await page.CleanCommand.ExecuteAsync(null);

        var confirm = _dialogs.Last<ConfirmDialogViewModel>()!;
        confirm.IsDanger.Should().BeTrue();
        confirm.Body.Should().Contain("nicht wiederherstellen").And.Contain("Endgültig gelöscht").And.Contain("Windows-Update-Reste").And.Contain("Administratorrechten");
        _service.Runs.Should().Equal(("CLEAN-001", CleanupMode.RecycleBin), ("CLEAN-003", CleanupMode.Permanent));
        _dialogs.Last<ResultDialogViewModel>()!.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task Declined_confirmation_deletes_nothing()
    {
        var dialogs = new ScriptedDialogs(d => d is not ConfirmDialogViewModel);
        var page = new CleanupPageViewModel(_service, dialogs, new Localizer(Localizer.German), readOnly: false);
        await page.ScanAsync();

        await page.CleanCommand.ExecuteAsync(null);

        _service.Runs.Should().BeEmpty();
    }

    [Fact]
    public async Task Permanent_mode_and_read_only_mode_are_respected()
    {
        var page = Page();
        await page.ScanAsync();
        page.UseRecycleBin = false;

        await page.CleanCommand.ExecuteAsync(null);
        _service.Runs.Should().OnlyContain(r => r.Mode == CleanupMode.Permanent);

        var readOnly = new CleanupPageViewModel(_service, _dialogs, new Localizer(Localizer.German), readOnly: true);
        await readOnly.ScanAsync();
        readOnly.CanClean.Should().BeFalse("nicht unterstuetzter Build: nur Lesemodus");
    }

    [Fact]
    public void Sizes_are_human_readable()
    {
        CleanupPageViewModel.Size(512).Should().EndWith("KB");
        CleanupPageViewModel.Size(5L << 20).Should().EndWith("MB");
        CleanupPageViewModel.Size(3L << 30).Should().EndWith("GB");
    }

    private CleanupPageViewModel Page() => new(_service, _dialogs, new Localizer(Localizer.German), readOnly: false);

    private static CleanupRowViewModel Row(CleanupPageViewModel page, string id) => page.Rows.Single(r => r.Category.Id == id);

    private sealed class FakeCleanup : ICleanupService
    {
        public List<(string Id, CleanupMode Mode)> Runs { get; } = [];

        public CleanupScan ScanCleanup(string categoryId) => categoryId switch
        {
            "CLEAN-002" => new CleanupScan { CategoryId = categoryId },
            "CLEAN-010" => new CleanupScan { CategoryId = categoryId, Blocked = "browser-open:chrome" },
            _ => new CleanupScan { CategoryId = categoryId, Files = 3, Bytes = 3000 },
        };

        public CleanupResult RunCleanup(string categoryId, CleanupMode mode)
        {
            Runs.Add((categoryId, mode));
            return new CleanupResult { CategoryId = categoryId, Mode = mode, DeletedFiles = 3, DeletedBytes = 3000 };
        }
    }
}
