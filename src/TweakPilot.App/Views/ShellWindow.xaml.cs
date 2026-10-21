using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TweakPilot.App.ViewModels;
using Wpf.Ui.Controls;

namespace TweakPilot.App.Views;

public partial class ShellWindow : FluentWindow
{
    private ShellViewModel? _shell;

    public ShellWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Fuer reproduzierbare Screenshots abschaltbar.</summary>
    public static bool AnimationsEnabled { get; set; } = true;

    /// <summary>
    /// Seitenwechsel: 200 ms, Ease-Out, nur Opazitaet und Versatz bis 8 Punkte (docs/DESIGN.md, Abschnitt 6).
    /// Ist in Windows "Animationseffekte" aus, wird nicht animiert.
    /// </summary>
    public static void FadeIn(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!AnimationsEnabled || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var shift = new TranslateTransform(0, 8);
        element.RenderTransform = shift;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = ease });
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_shell is not null)
        {
            _shell.PropertyChanged -= OnShellPropertyChanged;
        }

        _shell = e.NewValue as ShellViewModel;
        if (_shell is not null)
        {
            _shell.PropertyChanged += OnShellPropertyChanged;
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentPage))
        {
            FadeIn(PageHost);
        }
    }

    /// <summary>Fokus in den neuen Dialog; Escape bricht ab. Nach dem Schliessen kehrt der Fokus zur Seite zurueck.</summary>
    private void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement layer)
        {
            layer.KeyDown += (_, args) =>
            {
                if (args.Key == System.Windows.Input.Key.Escape && layer.DataContext is TweakPilot.App.Services.DialogViewModel dialog)
                {
                    dialog.Cancel();
                    args.Handled = true;
                }
            };
            layer.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
            layer.Unloaded += (_, _) => PageHost.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _shell?.UpdateWindowWidth(e.NewSize.Width);
    }
}
