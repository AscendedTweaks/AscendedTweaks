using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;

namespace AscendedTweaks.App;

/// <summary>
/// Small loading window while the app builds its pages. It runs on its own UI thread, so the spinner keeps turning while
/// the main thread is busy; the main window only appears once the first page has loaded. Colors match Tokens.Dark.xaml,
/// written out here because the app's resources belong to the main thread.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Window on its own thread; only seen when the app really starts.")]
internal sealed class StartupSplash
{
    private readonly ManualResetEventSlim _ready = new();
    private Window? _window;
    private Dispatcher? _dispatcher;

    private StartupSplash()
    {
    }

    public static StartupSplash Show(string title, string loading)
    {
        var splash = new StartupSplash();
        var thread = new Thread(() => splash.Run(title, loading)) { IsBackground = true, Name = "Startup splash" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        splash._ready.Wait(TimeSpan.FromSeconds(5));
        return splash;
    }

    public void Close()
    {
        _dispatcher?.InvokeAsync(() =>
        {
            _window?.Close();
            _dispatcher.InvokeShutdown();
        });
    }

    private void Run(string title, string loading)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _window = Build(title, loading);
        _window.ContentRendered += (_, _) => _ready.Set();
        _window.Closed += (_, _) => _ready.Set();
        _window.Show();
        Dispatcher.Run();
    }

    private static Window Build(string title, string loading)
    {
        var accent = Frozen(Color.FromRgb(0x8A, 0xB0, 0xFF));
        var spinner = new Path
        {
            // Three quarters of a circle, turning: the same light blue as links and toggles.
            Data = Geometry.Parse("M 14,2 A 12,12 0 1 1 2,14"),
            Stroke = accent,
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 28,
            Height = 28,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(),
            Margin = new Thickness(0, 0, 12, 0),
        };
        spinner.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });

        var header = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (Icon() is { } icon)
        {
            header.Children.Add(new Image { Source = icon, Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0) });
        }

        header.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Frozen(Color.FromRgb(0xF2, 0xF2, 0xF2)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var status = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
        status.Children.Add(spinner);
        status.Children.Add(new TextBlock
        {
            Text = loading,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = Frozen(Color.FromRgb(0xBE, 0xBE, 0xBE)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(header);
        content.Children.Add(status);

        return new Window
        {
            Title = title,
            Width = 380,
            Height = 200,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowActivated = true,
            Icon = Icon(),
            Content = new Border
            {
                Background = Frozen(Color.FromRgb(0x2B, 0x2D, 0x31)),
                BorderBrush = Frozen(Color.FromRgb(0x1E, 0x1F, 0x22)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = content,
            },
        };
    }

    /// <summary>Largest frame of the app icon; null if it cannot be read (the window still works without it).</summary>
    private static BitmapSource? Icon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ascendedtweaks.ico"));
            if (info is null)
            {
                return null;
            }

            using var stream = info.Stream;
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.MaxBy(f => f.PixelWidth);
            frame?.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
