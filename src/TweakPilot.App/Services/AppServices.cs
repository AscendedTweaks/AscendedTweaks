using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Microsoft.Win32;
using TweakPilot.Core;
using TweakPilot.Localization;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TweakPilot.App.Services;

public enum ThemeChoice
{
    Dark,
    Light,
    System,
}

/// <summary>Einstellungen der App pro Benutzer (%LOCALAPPDATA%\TweakPilot\settings.json).</summary>
public sealed class AppSettings
{
    /// <summary>Always English until the user picks a language in First steps or Settings (V3 section 2.1).</summary>
    public string Language { get; set; } = Localizer.English;

    public ThemeChoice Theme { get; set; } = ThemeChoice.Dark;

    public bool ExpertMode { get; set; }

    /// <summary>Stored under the old name so that existing settings files keep working.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("onboardingCompleted")]
    public bool FirstStepsCompleted { get; set; }

    /// <summary>V2 Abschnitt 7.3: einmal "Nur ausprobieren (portabel)" gewaehlt; die Frage kommt dann nicht wieder.</summary>
    public bool PortableChosen { get; set; }

    /// <summary>First start: English regardless of the Windows language (V3), theme from Windows, simple mode.</summary>
    public static AppSettings FirstStart() => new()
    {
        Language = Localizer.English,
        Theme = ThemeChoice.System,
        ExpertMode = false,
    };
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new(CoreJson.Options);

    public SettingsStore(string path) => Path = path;

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name, "settings.json");

    public string Path { get; }

    /// <summary>Fehlende oder beschaedigte Einstellungen fuehren zu Standardwerten, nie zu einem Absturz.</summary>
    public AppSettings Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Options) ?? new AppSettings()
                : AppSettings.FirstStart();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, Options));
    }
}

public interface IThemeService
{
    ThemeChoice Current { get; }

    void Apply(ThemeChoice choice);
}

/// <summary>Schaltet WPF-UI und die eigenen Design-Tokens zwischen Hell und Dunkel um.</summary>
public sealed class ThemeService : IThemeService
{
    private const int TokenDictionaryIndex = 2;

    public ThemeChoice Current { get; private set; } = ThemeChoice.Dark;

    public static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
    }

    public static ResourceDictionary LoadTokens(bool light)
    {
        return new ResourceDictionary { Source = new Uri($"pack://application:,,,/TweakPilot.App;component/Themes/Tokens.{(light ? "Light" : "Dark")}.xaml") };
    }

    /// <summary>Nur fuer Screenshots: Hochkontrast erzwingen, ohne die Windows-Einstellung zu aendern.</summary>
    internal static bool? HighContrastOverride { get; set; }

    public static bool HighContrast => HighContrastOverride ?? SystemParameters.HighContrast;

    /// <summary>
    /// Hochkontrast (Abschnitt 8.6): Alle Tokens kommen aus den Windows-Systemfarben, damit das gewaehlte Kontrastdesign gilt.
    /// Farbe ist dann nie das einzige Merkmal; Risiko- und Wirkungs-Badges haben immer Text und Symbol.
    /// </summary>
    public static ResourceDictionary HighContrastTokens()
    {
        var map = new (string Key, System.Windows.Media.Color Color)[]
        {
            ("Background", SystemColors.WindowColor),
            ("Surface", SystemColors.WindowColor),
            ("Elevated", SystemColors.WindowColor),
            ("Border", SystemColors.WindowTextColor),
            ("Accent", SystemColors.HighlightColor),
            ("OnAccent", SystemColors.HighlightTextColor),
            ("Success", SystemColors.WindowTextColor),
            ("Warning", SystemColors.WindowTextColor),
            ("Danger", SystemColors.WindowTextColor),
            ("TextPrimary", SystemColors.WindowTextColor),
            ("TextSecondary", SystemColors.WindowTextColor),
        };
        var tokens = new ResourceDictionary();
        foreach (var (key, color) in map)
        {
            tokens["Color." + key] = color;
            tokens["Brush." + key] = new System.Windows.Media.SolidColorBrush(color);
        }

        tokens["Brush.Scrim"] = new System.Windows.Media.SolidColorBrush(SystemColors.WindowColor) { Opacity = 0.8 };
        tokens["ApplicationBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(SystemColors.WindowColor);
        tokens["TextFillColorPrimaryBrush"] = new System.Windows.Media.SolidColorBrush(SystemColors.WindowTextColor);
        tokens["TextFillColorSecondaryBrush"] = new System.Windows.Media.SolidColorBrush(SystemColors.WindowTextColor);
        tokens["AccentTextFillColorPrimaryBrush"] = new System.Windows.Media.SolidColorBrush(SystemColors.HotTrackColor);
        tokens["CardBackgroundFillColorDefaultBrush"] = new System.Windows.Media.SolidColorBrush(SystemColors.WindowColor);
        return tokens;
    }

    /// <summary>Wechselt Windows in den Hochkontrast oder zurueck, wird das aktuelle Design neu angewendet.</summary>
    public void WatchHighContrast()
    {
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
            {
                Application.Current.Dispatcher.BeginInvoke(() => Apply(Current));
            }
        };
    }

    public void Apply(ThemeChoice choice)
    {
        Current = choice;
        if (HighContrast)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.HighContrast, WindowBackdropType.None, false);
            Application.Current.Resources.MergedDictionaries[TokenDictionaryIndex] = HighContrastTokens();
            return;
        }

        var light = choice == ThemeChoice.Light || (choice == ThemeChoice.System && SystemUsesLightTheme());
        var theme = light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None, false);
        var tokens = LoadTokens(light);
        ApplicationAccentColorManager.Apply((System.Windows.Media.Color)tokens["Color.Accent"], theme, false);

        // Die Token-Dictionary liegt an fester Stelle hinter den WPF-UI-Dictionaries und ueberschreibt deren Grundfarben.
        var merged = Application.Current.Resources.MergedDictionaries;
        merged[TokenDictionaryIndex] = tokens;
    }
}

/// <summary>XAML: Text="{loc:Loc banner.honesty}". Aktualisiert sich beim Sprachwechsel.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key) => Key = key;

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Localizer.Current, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
