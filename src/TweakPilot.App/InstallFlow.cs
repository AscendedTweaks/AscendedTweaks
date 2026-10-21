using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TweakPilot.App.Services;
using TweakPilot.Engine.State;
using TweakPilot.Engine.Windows;
using TweakPilot.Localization;

namespace TweakPilot.App;

/// <summary>
/// Started from a package folder outside %LOCALAPPDATA%\Programs\TweakPilot, the app asks once: "Install (recommended)" or
/// "Just try it (portable)". The dialog has the same language switch as First steps. "--uninstall" removes the installation
/// through a copy in %TEMP%, because a running program cannot delete its own folder.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Windows and process start; the logic lives in AppInstaller and is tested.")]
internal static class InstallFlow
{
    /// <summary>True: the app should exit (installed and restarted, or uninstalled).</summary>
    public static bool Handle(string[] args, AppSettings settings, SettingsStore store, string version)
    {
        var localizer = Localizer.Current;
        var installer = new AppInstaller(InstallPaths.ForCurrentUser(), new WindowsRegistryAccess(), new ShellShortcutWriter());
        var here = AppContext.BaseDirectory;

        if (args.Length >= 2 && args[0] == "--uninstall-run")
        {
            if (args.Length >= 4 && args[2] == "--wait" && int.TryParse(args[3], out var parent))
            {
                WaitForExit(parent);
            }

            Uninstall(installer, args[1], localizer);
            return true;
        }

        if (args.Contains("--uninstall"))
        {
            if (!installer.IsInstalledCopy(here))
            {
                Message(localizer["install.uninstall.notinstalled"]);
                return true;
            }

            // Start a temporary copy that removes the program folder after this process has ended.
            var copy = Path.Combine(Path.GetTempPath(), $"TweakPilot-Uninstall-{Guid.NewGuid():N}.exe");
            File.Copy(Environment.ProcessPath!, copy);
            var info = new ProcessStartInfo(copy) { UseShellExecute = false };
            info.ArgumentList.Add("--uninstall-run");
            info.ArgumentList.Add(installer.Paths.ProgramDirectory);
            info.ArgumentList.Add("--wait");
            info.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Process.Start(info)?.Dispose();
            return true;
        }

        if (args.Contains("--portable") || settings.PortableChosen || !AppInstaller.IsPackage(here) || installer.IsInstalledCopy(here))
        {
            return false;
        }

        void ChooseLanguage(string culture)
        {
            localizer.SetCulture(culture);
            settings.Language = culture;
            store.Save(settings);
        }

        if (Ask("install.title", "install.body", "install.install", "install.portable", null, ChooseLanguage).First)
        {
            try
            {
                installer.Install(here, version);
                Process.Start(new ProcessStartInfo(installer.Paths.InstalledExe) { UseShellExecute = false, WorkingDirectory = installer.Paths.ProgramDirectory })?.Dispose();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TweakPilot.Core.TweakPilotException)
            {
                Message(localizer.Format("install.failed", ("error", ex.Message)));
                return false;
            }
        }

        settings.PortableChosen = true;
        store.Save(settings);
        return false;
    }

    private static void Uninstall(AppInstaller installer, string directory, Localizer localizer)
    {
        var (confirmed, keep) = Ask("install.uninstall.title", "install.uninstall.body", "install.uninstall.confirm", "dialog.cancel", "install.uninstall.keep", null);
        if (!confirmed)
        {
            return;
        }

        var data = new[] { Path.GetDirectoryName(StateStore.DefaultUserRoot)!, Path.GetDirectoryName(StateStore.DefaultMachineRoot)! };
        var result = installer.Uninstall(directory, removeData: !keep, data);
        Message(result.Problems.Count == 0 ? localizer["install.uninstall.done"] : localizer.Format("install.uninstall.problems", ("problems", string.Join(Environment.NewLine, result.Problems))));
    }

    private static void WaitForExit(int processId)
    {
        try
        {
            using var parent = Process.GetProcessById(processId);
            parent.WaitForExit(15_000);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    /// <summary>Text bound to a language key, so that a language switch updates it live.</summary>
    private static Binding Loc(string key) => new($"[{key}]") { Source = Localizer.Current, Mode = BindingMode.OneWay };

    /// <summary>
    /// Small window before the main window: title, text, optional check box, one or two buttons, and (when <paramref name="language"/>
    /// is set) a language switch at the top right. Every argument is a language key. Returns (first button clicked, check box state).
    /// </summary>
    private static (bool First, bool Checked) Ask(string titleKey, string bodyKey, string firstKey, string? secondKey, string? checkboxKey, Action<string>? language, string? bodyText = null)
    {
        var result = (First: false, Checked: true);
        var window = new Window
        {
            SizeToContent = SizeToContent.Height,
            Width = 520,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("Brush.Background"),
        };
        window.SetBinding(Window.TitleProperty, Loc(titleKey));
        window.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, Loc(titleKey));
        var panel = new StackPanel { Margin = new Thickness(24) };

        if (language is not null)
        {
            var switcher = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (culture, label) in new[] { (Localizer.English, "English"), (Localizer.German, "Deutsch") })
            {
                var button = new Controls.ActionButton { Content = label, Style = (Style)Application.Current.FindResource("Button.Text"), MinHeight = 32, Margin = new Thickness(4, 0, 0, 0) };
                System.Windows.Automation.AutomationProperties.SetName(button, label);
                button.Click += (_, _) => language(culture);
                switcher.Children.Add(button);
            }

            panel.Children.Add(switcher);
        }

        var title = new TextBlock { Style = (Style)Application.Current.FindResource("Text.Subtitle"), Margin = new Thickness(0, 0, 0, 8) };
        title.SetBinding(TextBlock.TextProperty, Loc(titleKey));
        panel.Children.Add(title);
        var body = new TextBlock { Style = (Style)Application.Current.FindResource("Text.Simple.Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        if (bodyText is null)
        {
            body.SetBinding(TextBlock.TextProperty, Loc(bodyKey));
        }
        else
        {
            body.Text = bodyText;
        }

        panel.Children.Add(body);
        CheckBox? box = null;
        if (checkboxKey is not null)
        {
            box = new CheckBox { IsChecked = true, Margin = new Thickness(0, 0, 0, 16), MinHeight = 40 };
            box.SetBinding(ContentControl.ContentProperty, Loc(checkboxKey));
            panel.Children.Add(box);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var primary = new Controls.ActionButton { Style = (Style)Application.Current.FindResource("Button.Primary"), MinHeight = 40, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        primary.SetBinding(ContentControl.ContentProperty, Loc(firstKey));
        primary.Click += (_, _) =>
        {
            result.First = true;
            window.Close();
        };
        buttons.Children.Add(primary);
        if (secondKey is not null)
        {
            var other = new Controls.ActionButton { Style = (Style)Application.Current.FindResource("Button.Secondary"), MinHeight = 40, IsCancel = true };
            other.SetBinding(ContentControl.ContentProperty, Loc(secondKey));
            other.Click += (_, _) => window.Close();
            buttons.Children.Add(other);
        }

        panel.Children.Add(buttons);
        window.Content = panel;
        window.ShowDialog();
        result.Checked = box?.IsChecked != false;
        return result;
    }

    /// <summary>Message with one "Close" button; the text is already resolved (it may contain values).</summary>
    private static void Message(string text) => Ask("install.message.title", string.Empty, "dialog.close", null, null, null, text);
}
