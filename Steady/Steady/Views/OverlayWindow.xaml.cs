using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Steady.Helpers;
using Steady.Models;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using GdiSize = System.Drawing.Size;
using GdiPoint = System.Drawing.Point;

namespace Steady.Views;

public partial class OverlayWindow : Window
{
    private readonly List<Ellipse> _dots = [];
    private readonly List<Point> _homePositions = [];
    private readonly List<Point> _currentPositions = [];
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _contrastTimer;
    private MotionVector _targetMotion = MotionVector.Zero;
    private MotionVector _smoothedMotion = MotionVector.Zero;

    // Shared brush — RGB only (alpha=255); per-dot Opacity is controlled by edge fade
    private SolidColorBrush? _dotBrush;
    private bool _contrastIsLight; // hysteresis: true = bg is light → use dark dots

    // Edge distance function per dot: returns perpendicular distance from its home edge
    private readonly List<Func<Point, double>> _edgeDistFuncs = [];
    private double _screenW, _screenH;

    private AppSettings _settings = new();
    private const double LerpFactor = 0.12;
    private const double EdgeMargin = 20.0;

    public OverlayWindow()
    {
        InitializeComponent();
        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps
        };
        _renderTimer.Tick += OnRenderTick;

        _contrastTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500) // sample every 500ms
        };
        _contrastTimer.Tick += OnContrastTick;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = Win32Helper.GetWindowLong(hwnd, Win32Helper.GWL_EXSTYLE);
        Win32Helper.SetWindowLong(hwnd, Win32Helper.GWL_EXSTYLE,
            style | Win32Helper.WS_EX_TRANSPARENT | Win32Helper.WS_EX_LAYERED | Win32Helper.WS_EX_NOACTIVATE);
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        RebuildDots();
    }

    public void UpdateMotion(MotionVector motion) => _targetMotion = motion;

    public void SetBatterySaverMode(bool saver)
    {
        _renderTimer.Interval  = TimeSpan.FromMilliseconds(saver ? 33 : 16);  // 30 fps / 60 fps
        _contrastTimer.Interval = TimeSpan.FromMilliseconds(saver ? 2000 : 500);
    }

    public void SetVisible(bool visible)
    {
        if (visible)
        {
            PositionOnScreen();
            Show();
            _renderTimer.Start();
            _contrastTimer.Start();
        }
        else
        {
            _renderTimer.Stop();
            _contrastTimer.Stop();
            Hide();
        }
    }

    private void PositionOnScreen()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;

        RebuildDots();
    }

    private void RebuildDots()
    {
        DotCanvas.Children.Clear();
        _dots.Clear();
        _homePositions.Clear();
        _currentPositions.Clear();
        _edgeDistFuncs.Clear();

        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : Height;
        if (w == 0 || h == 0) return;

        _screenW = w;
        _screenH = h;

        // Brush holds RGB only (alpha=255); dot Opacity is the single opacity control
        var userColor = (Color)ColorConverter.ConvertFromString(_settings.DotColor);
        var initialColor = _settings.AdaptiveContrast
            ? Color.FromArgb(255, 255, 255, 255)
            : Color.FromArgb(255, userColor.R, userColor.G, userColor.B);
        _dotBrush = new SolidColorBrush(initialColor);

        var homes = ComputeHomePositions(w, h, _settings.DotSpacing);

        foreach (var home in homes)
        {
            // Determine which edge this dot belongs to by proximity
            double dTop    = home.Y;
            double dBottom = h - home.Y;
            double dLeft   = home.X;
            double dRight  = w - home.X;
            double minDist = Math.Min(Math.Min(dTop, dBottom), Math.Min(dLeft, dRight));

            Func<Point, double> edgeDist =
                minDist == dTop    ? p => p.Y             :
                minDist == dBottom ? p => _screenH - p.Y  :
                minDist == dLeft   ? p => p.X             :
                                     p => _screenW - p.X;
            _edgeDistFuncs.Add(edgeDist);

            var dot = new Ellipse
            {
                Width   = _settings.DotSize,
                Height  = _settings.DotSize,
                Fill    = _dotBrush,
                Opacity = _settings.DotOpacity
            };
            DotCanvas.Children.Add(dot);
            _dots.Add(dot);
            _homePositions.Add(home);
            _currentPositions.Add(home);
        }
    }

    // Density-based layout: dot count per edge is derived from that edge's own
    // length and the desired spacing, so spacing stays uniform on every edge
    // regardless of screen size or aspect ratio. Smaller spacing = denser field.
    private const double TopMargin = 20.0;
    private const double SideMargin = 20.0;
    // Bottom dots sit higher than the others so they clear the screen edge / taskbar.
    private const double BottomMargin = 48.0;

    private static List<Point> ComputeHomePositions(double w, double h, double spacing)
    {
        var positions = new List<Point>();
        spacing = Math.Max(20.0, spacing); // guard against zero/too-dense

        double bottomY = h - BottomMargin;
        double usableW = Math.Max(1.0, w - 2 * SideMargin);
        double usableH = Math.Max(1.0, bottomY - TopMargin);

        int cols = Math.Max(2, (int)Math.Round(usableW / spacing) + 1); // top & bottom
        int rows = Math.Max(2, (int)Math.Round(usableH / spacing) + 1); // left & right

        // Top + bottom edges (include the corners). Bottom uses its larger margin.
        for (int i = 0; i < cols; i++)
        {
            double x = SideMargin + usableW * i / (cols - 1);
            positions.Add(new Point(x, TopMargin));
            positions.Add(new Point(x, bottomY));
        }
        // Left + right edges (skip corners already placed above)
        for (int i = 1; i < rows - 1; i++)
        {
            double y = TopMargin + usableH * i / (rows - 1);
            positions.Add(new Point(SideMargin, y));
            positions.Add(new Point(w - SideMargin, y));
        }

        return positions;
    }

    private void OnContrastTick(object? sender, EventArgs e)
    {
        if (_dotBrush == null) return;
        byte alpha = (byte)(_settings.DotOpacity * 255);

        if (!_settings.AdaptiveContrast)
        {
            // Restore user-chosen color (alpha=255; opacity controlled per-dot)
            var userColor = (Color)ColorConverter.ConvertFromString(_settings.DotColor);
            _dotBrush.Color = Color.FromArgb(255, userColor.R, userColor.G, userColor.B);
            return;
        }

        double lum = SampleEdgeLuminance();

        // Hysteresis: switch only when luminance crosses the 0.45 / 0.55 band to prevent flicker
        if (!_contrastIsLight && lum > 0.55) _contrastIsLight = true;
        else if (_contrastIsLight && lum < 0.45) _contrastIsLight = false;

        // พื้นสว่าง → จุดเข้ม, พื้นมืด → จุดขาว (alpha=255; Ellipse.Opacity ควบคุม transparency)
        _dotBrush.Color = _contrastIsLight
            ? Color.FromArgb(255, 30, 30, 30)
            : Color.FromArgb(255, 255, 255, 255);
    }

    /// <summary>
    /// Samples four 30×30 corner regions of the primary screen and returns average relative luminance [0,1].
    /// Uses a downsampled 4×4 thumbnail per region for performance (~64 pixels total).
    /// </summary>
    private static double SampleEdgeLuminance()
    {
        try
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            const int sampleSize = 30;

            var corners = new[]
            {
                new System.Drawing.Rectangle(screen.Left,                   screen.Top,                   sampleSize, sampleSize),
                new System.Drawing.Rectangle(screen.Right - sampleSize,    screen.Top,                   sampleSize, sampleSize),
                new System.Drawing.Rectangle(screen.Left,                   screen.Bottom - sampleSize,   sampleSize, sampleSize),
                new System.Drawing.Rectangle(screen.Right - sampleSize,    screen.Bottom - sampleSize,   sampleSize, sampleSize),
            };

            double totalLum = 0;
            int pixelCount = 0;

            foreach (var region in corners)
            {
                using var bmp = new System.Drawing.Bitmap(sampleSize, sampleSize);
                using var g = System.Drawing.Graphics.FromImage(bmp);
                g.CopyFromScreen(new GdiPoint(region.X, region.Y), GdiPoint.Empty, new GdiSize(sampleSize, sampleSize));

                // Downsample to 4×4 for speed
                using var small = new System.Drawing.Bitmap(bmp, 4, 4);
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        var px = small.GetPixel(x, y);
                        // Relative luminance (sRGB coefficients)
                        totalLum += 0.2126 * px.R + 0.7152 * px.G + 0.0722 * px.B;
                        pixelCount++;
                    }
            }

            return totalLum / (pixelCount * 255.0);
        }
        catch
        {
            return 0.0; // assume dark on error → use white dots
        }
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        _smoothedMotion = _smoothedMotion.Lerp(_targetMotion, LerpFactor);

        // Direction mapping (spec): เบรก→ขึ้น, เร่ง→ลง, เลี้ยวซ้าย→ขวา, เลี้ยวขวา→ซ้าย
        double offsetX = _smoothedMotion.X * _settings.MaxDotOffset * _settings.IntensityMultiplier;
        double offsetY = -_smoothedMotion.Y * _settings.MaxDotOffset * _settings.IntensityMultiplier;

        double baseSize    = _settings.DotSize;
        double baseOpacity = _settings.DotOpacity;
        double maxFadeDist = Math.Max(1.0, _settings.MaxDotOffset * _settings.IntensityMultiplier);

        for (int i = 0; i < _dots.Count; i++)
        {
            var target = new Point(
                _homePositions[i].X + offsetX,
                _homePositions[i].Y + offsetY);

            // Spring interpolation toward target
            _currentPositions[i] = new Point(
                _currentPositions[i].X + (target.X - _currentPositions[i].X) * LerpFactor * 2,
                _currentPositions[i].Y + (target.Y - _currentPositions[i].Y) * LerpFactor * 2);

            // Edge fade: displacement from home edge → smaller + more transparent
            double distFromEdge = _edgeDistFuncs.Count > i
                ? _edgeDistFuncs[i](_currentPositions[i])
                : EdgeMargin;
            double displacement = Math.Max(0.0, distFromEdge - EdgeMargin);
            double fadeFactor   = Math.Clamp(1.0 - displacement / maxFadeDist, 0.0, 1.0);

            // Size: 50%–100% of base (large at edge, small when displaced toward center)
            double size = baseSize * (0.5 + 0.5 * fadeFactor);
            // Opacity: 15%–100% of base (visible at edge, nearly transparent when displaced)
            double opacity = baseOpacity * (0.15 + 0.85 * fadeFactor);

            _dots[i].Width   = size;
            _dots[i].Height  = size;
            _dots[i].Opacity = opacity;

            System.Windows.Controls.Canvas.SetLeft(_dots[i], _currentPositions[i].X - size / 2);
            System.Windows.Controls.Canvas.SetTop(_dots[i], _currentPositions[i].Y - size / 2);
        }
    }
}
