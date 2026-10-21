using System.Windows;
using System.Windows.Controls;

namespace TweakPilot.App.Views;

public partial class SettingsPage : UserControl
{
    public static readonly DependencyProperty ShowExpertModeProperty =
        DependencyProperty.Register(nameof(ShowExpertMode), typeof(bool), typeof(SettingsPage), new PropertyMetadata(true));

    public SettingsPage()
    {
        InitializeComponent();
    }

    /// <summary>Im First steps nur Sprache und Design, ohne Expertenmodus.</summary>
    public bool ShowExpertMode
    {
        get => (bool)GetValue(ShowExpertModeProperty);
        set => SetValue(ShowExpertModeProperty, value);
    }
}
