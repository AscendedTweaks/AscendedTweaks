using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using AscendedTweaks.Core;
using AscendedTweaks.Localization;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AscendedTweaks.App.Services;

/// <summary>Einstellungen der App pro Benutzer (%LOCALAPPDATA%\AscendedTweaks\settings.json).</summary>
public sealed class AppSettings
{
    /// <summary>Always English until the user picks a language in First steps or Settings (V3 section 2.1).</summary>
    public string Language { get; set; } = Localizer.English;

    /// <summary>
    /// Ascended Tweaks only has the dark design. Older settings files still contain "theme"; it is read so that such a file
    /// stays valid (unknown fields are refused) and dropped on the next save.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("theme")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyTheme { get; set; }

    public bool ExpertMode { get; set; }

    /// <summary>Stored under the old name so that existing settings files keep working.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("onboardingCompleted")]
    public bool FirstStepsCompleted { get; set; }

    /// <summary>V2 Abschnitt 7.3: einmal "Nur ausprobieren (portabel)" gewaehlt; die Frage kommt dann nicht wieder.</summary>
    public bool PortableChosen { get; set; }

    /// <summary>First start: English regardless of the Windows language (V3), simple mode.</summary>
    public static AppSettings FirstStart() => new()
    {
        Language = Localizer.English,
        ExpertMode = false,
    };
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new(CoreJson.Options);

    public SettingsStore(string path) => Path = path;

    public static string DefaultPath =>
        System.IO.Path.Combine(ProductInfo.DataDirectory(Environment.SpecialFolder.LocalApplicationData), "settings.json");

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
        settings.LegacyTheme = null;
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, Options));
    }
}

public interface IThemeService
{
    /// <summary>Applies the dark design, or the Windows high contrast colors while high contrast is on.</summary>
    void Apply();
}

/// <summary>Applies the dark design tokens (the only design) or, in Windows high contrast, the system colors.</summary>
public sealed class ThemeService : IThemeService
{
    private const int TokenDictionaryIndex = 2;

    public static ResourceDictionary LoadTokens()
    {
        return new ResourceDictionary { Source = new Uri("pack://application:,,,/AscendedTweaks.App;component/Themes/Tokens.Dark.xaml") };
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
        tokens["Brush.AccentTint"] = new System.Windows.Media.SolidColorBrush(SystemColors.HighlightColor) { Opacity = 0.3 };

        // Preset colors and glow follow the contrast theme: symbols and edges in the text color, plain tiles, no glow.
        foreach (var hue in new[] { "Mint", "Sky", "Coral", "Lime", "Lilac", "Rose", "Teal" })
        {
            tokens[$"Brush.Hue.{hue}"] = new System.Windows.Media.SolidColorBrush(SystemColors.WindowTextColor);
            tokens[$"Brush.Hue.{hue}.Tint"] = System.Windows.Media.Brushes.Transparent;
        }

        tokens["Glow.Accent"] = null;
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
                Application.Current.Dispatcher.BeginInvoke(Apply);
            }
        };
    }

    public void Apply()
    {
        if (HighContrast)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.HighContrast, WindowBackdropType.None, false);
            Application.Current.Resources.MergedDictionaries[TokenDictionaryIndex] = HighContrastTokens();
            return;
        }

        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.None, false);
        var tokens = LoadTokens();
        ApplicationAccentColorManager.Apply((System.Windows.Media.Color)tokens["Color.Accent"], ApplicationTheme.Dark, false);

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
