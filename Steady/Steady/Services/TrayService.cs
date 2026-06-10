using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Steady.Helpers;

namespace Steady.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private ToolStripMenuItem _toggleItem = null!;
    private bool _isEnabled;
    private Icon? _iconOn;
    private Icon? _iconOff;

    public event EventHandler? ToggleRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayService()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = $"{AppInfo.Display} — Focal Point for Windows",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);
        BuildContextMenu();
        UpdateIcon(false);
    }

    private void BuildContextMenu()
    {
        _toggleItem = new ToolStripMenuItem("Enable Steady", null, (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty));

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(AppInfo.Display) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon.ContextMenuStrip = menu;
    }

    public void UpdateState(bool isEnabled)
    {
        _isEnabled = isEnabled;
        _toggleItem.Text = isEnabled ? "Disable Steady  (Ctrl+Alt+M)" : "Enable Steady  (Ctrl+Alt+M)";
        UpdateIcon(isEnabled);
    }

    private void UpdateIcon(bool isEnabled)
    {
        var icon = isEnabled
            ? GetIcon(ref _iconOn, "steady.ico")
            : GetIcon(ref _iconOff, "steady_off.ico");
        if (icon != null)
        {
            _notifyIcon.Icon = icon;
        }
    }

    // Loads (and caches) a tray icon from the Assets folder beside the exe,
    // picking the DPI-appropriate small-icon size. Falls back to a drawn glyph
    // if the file is missing.
    private static Icon? GetIcon(ref Icon? cache, string fileName)
    {
        if (cache != null)
        {
            return cache;
        }
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (File.Exists(path))
            {
                cache = new Icon(path, SystemInformation.SmallIconSize);
                return cache;
            }
        }
        catch
        {
            // ignore and fall through to drawn fallback
        }

        cache = DrawFallbackIcon(!fileName.Contains("off"));
        return cache;
    }

    /// <summary>
    /// Draws the tray icon programmatically:
    /// Enabled  — blue→purple gradient rounded tile + white screen frame + motion-cue dots
    /// Disabled — gray tile with dim frame and muted dots
    /// This is used as a fallback when steady.ico / steady_off.ico are not present in Assets/.
    /// </summary>
    private static Icon DrawFallbackIcon(bool isEnabled)
    {
        const int size = 16;
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighQuality;

        var bounds = new Rectangle(0, 0, size - 1, size - 1);

        if (isEnabled)
        {
            // Blue (#5B86E6) → purple (#9200D0) gradient, top-left to bottom-right
            using var grad = new LinearGradientBrush(
                new Rectangle(0, 0, size, size),
                Color.FromArgb(0x5B, 0x86, 0xE6),
                Color.FromArgb(0x92, 0x00, 0xD0),
                135f);
            using var roundPath = RoundedRectPath(bounds, 3);
            g.FillPath(grad, roundPath);

            // White screen border
            g.FillRectangle(Brushes.White, 3, 3, 10, 9);
            // Screen content (light blue-gray)
            g.FillRectangle(new SolidBrush(Color.FromArgb(0xC5, 0xD8, 0xEA)), 4, 4, 8, 7);

            // Left-edge motion-cue dots (white): large + small
            g.FillEllipse(Brushes.White, 0.5f, 3.5f, 2.5f, 2.5f);
            g.FillEllipse(Brushes.White, 1.0f, 8.0f, 1.5f, 1.5f);
            // Right-edge motion-cue dots (white)
            g.FillEllipse(Brushes.White, 13.0f, 3.5f, 2.5f, 2.5f);
            g.FillEllipse(Brushes.White, 13.5f, 8.0f, 1.5f, 1.5f);
        }
        else
        {
            // Uniform dark-gray tile
            using var roundPath = RoundedRectPath(bounds, 3);
            g.FillPath(new SolidBrush(Color.FromArgb(0x64, 0x64, 0x6E)), roundPath);

            // Dim screen frame
            g.FillRectangle(new SolidBrush(Color.FromArgb(0x9A, 0x9A, 0xA2)), 3, 3, 10, 9);
            g.FillRectangle(new SolidBrush(Color.FromArgb(0x7A, 0x7A, 0x82)), 4, 4, 8, 7);

            // Muted dots
            g.FillEllipse(new SolidBrush(Color.FromArgb(0xB8, 0xB8, 0xC0)), 0.5f, 3.5f, 2.5f, 2.5f);
            g.FillEllipse(new SolidBrush(Color.FromArgb(0xA8, 0xA8, 0xB0)), 1.0f, 8.0f, 1.5f, 1.5f);
            g.FillEllipse(new SolidBrush(Color.FromArgb(0xB8, 0xB8, 0xC0)), 13.0f, 3.5f, 2.5f, 2.5f);
            g.FillEllipse(new SolidBrush(Color.FromArgb(0xA8, 0xA8, 0xB0)), 13.5f, 8.0f, 1.5f, 1.5f);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath RoundedRectPath(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info) =>
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _iconOn?.Dispose();
        _iconOff?.Dispose();
    }
}
