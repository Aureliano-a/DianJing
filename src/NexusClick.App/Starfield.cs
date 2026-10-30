using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace NexusClick.App;

/// <summary>Decorative, input-transparent background with a capped render rate.</summary>
public sealed class Starfield : FrameworkElement
{
    public static readonly DependencyProperty IsAnimatedProperty = DependencyProperty.Register(
        nameof(IsAnimated), typeof(bool), typeof(Starfield),
        new FrameworkPropertyMetadata(true, OnAnimationChanged));

    private readonly record struct Star(double X, double Y, double Radius, double Phase, double Drift, int Hue);
    private readonly Star[] _stars;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastFrame;
    private double _animationSeconds;
    private bool _subscribed;
    private static readonly Brush[,] StarBrushes = CreateStarBrushes();
    private static readonly Brush NebulaBlue = MakeNebula(Color.FromArgb(110, 25, 101, 176));
    private static readonly Brush NebulaViolet = MakeNebula(Color.FromArgb(110, 102, 42, 172));

    public bool IsAnimated { get => (bool)GetValue(IsAnimatedProperty); set => SetValue(IsAnimatedProperty, value); }

    public Starfield()
    {
        IsHitTestVisible = false;
        var random = new Random(71204);
        _stars = Enumerable.Range(0, 175).Select(_ => new Star(
            random.NextDouble(), random.NextDouble(), 0.6 + random.NextDouble() * 1.15,
            random.NextDouble() * Math.PI * 2, 0.25 + random.NextDouble() * 0.6, random.Next(3))).ToArray();
        Loaded += (_, _) => UpdateRendering();
        Unloaded += (_, _) => UpdateRendering();
        IsVisibleChanged += (_, _) => UpdateRendering();
    }

    private static void OnAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var field = (Starfield)d;
        field.UpdateRendering();
        field.InvalidateVisual();
    }

    private void UpdateRendering()
    {
        var shouldSubscribe = IsLoaded && IsVisible && IsAnimated;
        if (shouldSubscribe == _subscribed) return;
        if (shouldSubscribe) CompositionTarget.Rendering += OnRendering;
        else CompositionTarget.Rendering -= OnRendering;
        _subscribed = shouldSubscribe;
        _lastFrame = TimeSpan.Zero;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (Window.GetWindow(this)?.WindowState == WindowState.Minimized) { _lastFrame = TimeSpan.Zero; return; }
        var now = _clock.Elapsed;
        if (_lastFrame == TimeSpan.Zero) { _lastFrame = now; return; }
        var elapsed = now - _lastFrame;
        if (elapsed < TimeSpan.FromMilliseconds(33)) return;
        _animationSeconds += Math.Min(elapsed.TotalSeconds, .1);
        _lastFrame = now;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        dc.DrawRectangle(NebulaBlue, null, new Rect(width * 0.10, -height * 0.38, width * 0.80, height * 1.05));
        dc.DrawRectangle(NebulaViolet, null, new Rect(width * 0.38, height * 0.20, width * 0.80, height * 1.0));
        var seconds = _animationSeconds;
        foreach (var star in _stars)
        {
            var x = (star.X * width + seconds * star.Drift) % width;
            var y = (star.Y * height + seconds * star.Drift * 0.18) % height;
            var brightness = (int)Math.Clamp((Math.Sin(seconds * 0.85 + star.Phase) + 1) * 2.5, 0, 5);
            dc.DrawEllipse(StarBrushes[star.Hue, brightness], null, new Point(x, y), star.Radius, star.Radius);
        }
    }

    private static Brush MakeNebula(Color color)
    {
        var brush = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Brush[,] CreateStarBrushes()
    {
        var result = new Brush[3, 6];
        var colors = new[] { Color.FromRgb(199, 232, 255), Color.FromRgb(119, 216, 255), Color.FromRgb(202, 174, 255) };
        for (var hue = 0; hue < 3; hue++)
        for (var level = 0; level < 6; level++)
        {
            var color = colors[hue];
            color.A = (byte)(55 + level * 34);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            result[hue, level] = brush;
        }
        return result;
    }
}
