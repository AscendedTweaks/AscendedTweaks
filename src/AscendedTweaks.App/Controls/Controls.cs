using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace AscendedTweaks.App.Controls;

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

/// <summary>
/// Button with a "running" state (spinner, no further clicks). With <see cref="HoldDuration"/> it only triggers after
/// being held (mouse, Space or Enter), for actions that reset everything; the template part PART_HoldFill grows smoothly
/// while held and shrinks back when let go early.
/// </summary>
[TemplatePart(Name = HoldFillPart, Type = typeof(FrameworkElement))]
public sealed class ActionButton : System.Windows.Controls.Button
{
    public const string HoldFillPart = "PART_HoldFill";

    public static readonly DependencyProperty IsRunningProperty =
        DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(ActionButton), new PropertyMetadata(false));

    public static readonly DependencyProperty HoldDurationProperty =
        DependencyProperty.Register(nameof(HoldDuration), typeof(TimeSpan), typeof(ActionButton), new PropertyMetadata(TimeSpan.Zero));

    /// <summary>Small second line inside the button ("Hold to confirm"); hidden when empty.</summary>
    public static readonly DependencyProperty HoldHintProperty =
        DependencyProperty.Register(nameof(HoldHint), typeof(string), typeof(ActionButton), new PropertyMetadata(null));

    private static readonly TimeSpan ShrinkTime = TimeSpan.FromMilliseconds(180);
    private FrameworkElement? _fill;
    private bool _pressed;
    private bool _holding;
    private bool _completing;

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    public TimeSpan HoldDuration
    {
        get => (TimeSpan)GetValue(HoldDurationProperty);
        set => SetValue(HoldDurationProperty, value);
    }

    public string? HoldHint
    {
        get => (string?)GetValue(HoldHintProperty);
        set => SetValue(HoldHintProperty, value);
    }

    private bool UsesHold => HoldDuration > TimeSpan.Zero;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _fill = GetTemplateChild(HoldFillPart) as FrameworkElement;
    }

    protected override void OnClick()
    {
        if (IsRunning)
        {
            return;
        }

        // With a hold time, the click of a short press is ignored; screen readers invoke without pressing and still work.
        if (UsesHold && _pressed && !_completing)
        {
            return;
        }

        base.OnClick();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (UsesHold && IsEnabled && !IsRunning)
        {
            _pressed = true;
            StartHold();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _pressed = false;
        CancelHold();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        CancelHold();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _pressed = false;
        CancelHold();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (UsesHold && e.Key is Key.Space or Key.Enter)
        {
            e.Handled = true;
            if (!e.IsRepeat && IsEnabled && !IsRunning && !_holding)
            {
                _pressed = true;
                StartHold();
            }

            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (UsesHold && e.Key is Key.Space or Key.Enter)
        {
            e.Handled = true;
            _pressed = false;
            CancelHold();
            return;
        }

        base.OnKeyUp(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _pressed = false;
        CancelHold();
    }

    /// <summary>Grows the fill from where it is to the full width in the time that is left; the animation drives the click.</summary>
    private void StartHold()
    {
        _holding = true;
        if (_fill is null || ActualWidth <= 0)
        {
            return;
        }

        var from = double.IsNaN(_fill.Width) ? 0 : _fill.ActualWidth;
        var remaining = TimeSpan.FromTicks((long)(HoldDuration.Ticks * Math.Clamp(1 - (from / ActualWidth), 0, 1)));
        var grow = new DoubleAnimation(from, ActualWidth, new Duration(remaining)) { FillBehavior = FillBehavior.HoldEnd };
        grow.Completed += (_, _) =>
        {
            if (!_holding)
            {
                return;
            }

            _holding = false;
            _completing = true;
            try
            {
                OnClick();
            }
            finally
            {
                _completing = false;
                Shrink();
            }
        };
        _fill.BeginAnimation(WidthProperty, grow, HandoffBehavior.SnapshotAndReplace);
    }

    private void CancelHold()
    {
        if (!_holding)
        {
            return;
        }

        _holding = false;
        Shrink();
    }

    private void Shrink()
    {
        if (_fill is null)
        {
            return;
        }

        var shrink = new DoubleAnimation(_fill.ActualWidth, 0, new Duration(ShrinkTime))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        _fill.BeginAnimation(WidthProperty, shrink, HandoffBehavior.SnapshotAndReplace);
    }
}
