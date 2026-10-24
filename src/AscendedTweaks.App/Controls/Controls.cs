using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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

/// <summary>Schaltflaeche mit Zustand "Laufend" (Spinner, keine weiteren Klicks).</summary>
/// <summary>
/// Press-and-hold timing for buttons with far-reaching effects ("Undo everything"): progress from 0 to 1 over the hold
/// time, done once it reaches 1; letting go before resets it. Separate from the control so it can be tested without a window.
/// </summary>
public sealed class HoldGesture
{
    private readonly TimeSpan _duration;
    private DateTime? _start;

    public HoldGesture(TimeSpan duration) => _duration = duration;

    public bool IsHolding => _start is not null;

    public void Start(DateTime now) => _start = now;

    public void Cancel() => _start = null;

    /// <summary>Progress at <paramref name="now"/>, 0 when not holding; ends the hold when it reaches 1.</summary>
    public double Progress(DateTime now, out bool completed)
    {
        completed = false;
        if (_start is not { } start)
        {
            return 0;
        }

        var progress = _duration <= TimeSpan.Zero ? 1 : Math.Clamp((now - start) / _duration, 0, 1);
        if (progress >= 1)
        {
            completed = true;
            _start = null;
        }

        return progress;
    }
}

public sealed class ActionButton : System.Windows.Controls.Button
{
    public static readonly DependencyProperty IsRunningProperty =
        DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(ActionButton), new PropertyMetadata(false));

    /// <summary>When set, the button only triggers after being held this long (mouse, Space or Enter); a short press does nothing.</summary>
    public static readonly DependencyProperty HoldDurationProperty =
        DependencyProperty.Register(nameof(HoldDuration), typeof(TimeSpan), typeof(ActionButton), new PropertyMetadata(TimeSpan.Zero));

    private static readonly DependencyPropertyKey HoldProgressKey =
        DependencyProperty.RegisterReadOnly(nameof(HoldProgress), typeof(double), typeof(ActionButton), new PropertyMetadata(0.0));

    public static readonly DependencyProperty HoldProgressProperty = HoldProgressKey.DependencyProperty;

    private HoldGesture? _hold;
    private DispatcherTimer? _timer;
    private bool _pressed;
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

    /// <summary>0 to 1 while held; the template fills the button with it.</summary>
    public double HoldProgress => (double)GetValue(HoldProgressProperty);

    private bool UsesHold => HoldDuration > TimeSpan.Zero;

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
            if (!e.IsRepeat && IsEnabled && !IsRunning && _hold?.IsHolding != true)
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

    private void StartHold()
    {
        _hold = new HoldGesture(HoldDuration);
        _hold.Start(DateTime.UtcNow);
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, OnHoldTick, Dispatcher);
        _timer.Start();
    }

    private void OnHoldTick(object? sender, EventArgs e)
    {
        if (_hold is null)
        {
            return;
        }

        var progress = _hold.Progress(DateTime.UtcNow, out var completed);
        SetValue(HoldProgressKey, progress);
        if (!completed)
        {
            return;
        }

        _timer?.Stop();
        _completing = true;
        try
        {
            OnClick();
        }
        finally
        {
            _completing = false;
            SetValue(HoldProgressKey, 0.0);
        }
    }

    private void CancelHold()
    {
        _hold?.Cancel();
        _timer?.Stop();
        SetValue(HoldProgressKey, 0.0);
    }
}
