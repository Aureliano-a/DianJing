using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace NexusClick.App;

/// <summary>Decorative globe using a static NASA Earth mosaic, not live imagery.</summary>
public sealed class PlanetArtwork : Grid
{
    public static readonly DependencyProperty IsAnimatedProperty = DependencyProperty.Register(
        nameof(IsAnimated), typeof(bool), typeof(PlanetArtwork),
        new FrameworkPropertyMetadata(true, OnAnimationChanged));

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AxisAngleRotation3D _rotation = new(new Vector3D(0, 1, 0), -100);
    private TimeSpan _lastFrame;
    private bool _subscribed;

    public bool IsAnimated { get => (bool)GetValue(IsAnimatedProperty); set => SetValue(IsAnimatedProperty, value); }

    public PlanetArtwork()
    {
        IsHitTestVisible = false;
        var atmosphere = new RadialGradientBrush(Color.FromArgb(100, 63, 154, 255), Color.FromArgb(0, 63, 154, 255));
        atmosphere.Freeze();
        Children.Add(new Ellipse { Fill = atmosphere, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch });
        var viewport = new Viewport3D
        {
            Camera = new PerspectiveCamera(new Point3D(0, 0, 3.7), new Vector3D(0, 0, -3.7), new Vector3D(0, 1, 0), 40),
            ClipToBounds = true
        };
        var texture = new BitmapImage();
        texture.BeginInit();
        texture.UriSource = new Uri("pack://application:,,,/NexusClick;component/Assets/earth-nasa.jpg", UriKind.Absolute);
        texture.CacheOption = BitmapCacheOption.OnLoad;
        texture.EndInit();
        texture.Freeze();
        var material = new DiffuseMaterial(new ImageBrush(texture));
        var earth = new GeometryModel3D(CreateSphere(64, 32), material)
        {
            Transform = new RotateTransform3D(_rotation)
        };
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(100, 100, 110)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(210, 210, 210), new Vector3D(.4, -.3, -1)));
        scene.Children.Add(earth);
        viewport.Children.Add(new ModelVisual3D { Content = scene });
        Children.Add(viewport);
        Loaded += (_, _) => UpdateRendering();
        Unloaded += (_, _) => UpdateRendering();
        IsVisibleChanged += (_, _) => UpdateRendering();
    }

    private static MeshGeometry3D CreateSphere(int longitudeSegments, int latitudeSegments)
    {
        var mesh = new MeshGeometry3D();
        for (var lat = 0; lat <= latitudeSegments; lat++)
        {
            double v = (double)lat / latitudeSegments;
            double phi = Math.PI * v;
            for (var lon = 0; lon <= longitudeSegments; lon++)
            {
                double u = (double)lon / longitudeSegments;
                double theta = 2 * Math.PI * u - Math.PI;
                double x = Math.Sin(phi) * Math.Sin(theta);
                double y = Math.Cos(phi);
                double z = Math.Sin(phi) * Math.Cos(theta);
                mesh.Positions.Add(new Point3D(x, y, z));
                mesh.Normals.Add(new Vector3D(x, y, z));
                mesh.TextureCoordinates.Add(new Point(u, v));
            }
        }
        int width = longitudeSegments + 1;
        for (var lat = 0; lat < latitudeSegments; lat++)
        for (var lon = 0; lon < longitudeSegments; lon++)
        {
            int a = lat * width + lon, b = a + 1, c = a + width, d = c + 1;
            mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(d);
        }
        mesh.Freeze();
        return mesh;
    }

    private static void OnAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PlanetArtwork)d).UpdateRendering();

    private void UpdateRendering()
    {
        bool shouldSubscribe = IsLoaded && IsVisible && IsAnimated;
        if (shouldSubscribe == _subscribed) return;
        if (shouldSubscribe) CompositionTarget.Rendering += OnRendering;
        else CompositionTarget.Rendering -= OnRendering;
        _subscribed = shouldSubscribe;
        _lastFrame = TimeSpan.Zero;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        if (Window.GetWindow(this)?.WindowState == WindowState.Minimized) { _lastFrame = TimeSpan.Zero; return; }
        if (_lastFrame == TimeSpan.Zero) { _lastFrame = now; return; }
        var elapsed = now - _lastFrame;
        if (elapsed < TimeSpan.FromMilliseconds(33)) return;
        _lastFrame = now;
        _rotation.Angle = (_rotation.Angle + Math.Min(elapsed.TotalSeconds, .1) * 3) % 360;
    }
}
