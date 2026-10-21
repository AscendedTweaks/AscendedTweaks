using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace TweakPilot.App.Controls;

public enum BadgeTone
{
    Neutral,
    Accent,
    Success,
    Warning,
    Danger,
}

/// <summary>Badge mit Symbol und Text. Farbe ist nie das einzige Merkmal (docs/DESIGN.md, Abschnitt 2).</summary>
public sealed class Badge : Control
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(Badge), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SymbolProperty =
        DependencyProperty.Register(nameof(Symbol), typeof(SymbolRegular), typeof(Badge), new PropertyMetadata(SymbolRegular.Info16));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(BadgeTone), typeof(Badge), new PropertyMetadata(BadgeTone.Neutral));

    public static readonly DependencyProperty FilledProperty =
        DependencyProperty.Register(nameof(Filled), typeof(bool), typeof(Badge), new PropertyMetadata(false));

    static Badge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(typeof(Badge)));
        FocusableProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(false));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public SymbolRegular Symbol
    {
        get => (SymbolRegular)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public BadgeTone Tone
    {
        get => (BadgeTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    /// <summary>Gefuellte Badges (Risiko) haben eine leicht getoente Flaeche, Umriss-Badges (Wirkung) nicht.</summary>
    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }
}

/// <summary>Schaltflaeche mit Zustand "Laufend" (Spinner, keine weiteren Klicks).</summary>
public sealed class ActionButton : System.Windows.Controls.Button
{
    public static readonly DependencyProperty IsRunningProperty =
        DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(ActionButton), new PropertyMetadata(false));

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    protected override void OnClick()
    {
        if (!IsRunning)
        {
            base.OnClick();
        }
    }
}
