using System.Threading;
using System.Windows;
using Steady.Models;
using Steady.Services;
using Steady.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace Steady;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    private SettingsService _settingsService = null!;
    private SensorManager _sensorManager = null!;
    private TrayService _trayService = null!;
    private HotkeyService _hotkeyService = null!;
    private OverlayWindow _overlay = null!;
    private SettingsWindow _settingsWindow = null!;
    private PowerMonitorService _powerMonitor = null!;

    // Hidden message-only window to receive Win32 hotkey messages
    private Window _messageWindow = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance guard
        _singleInstanceMutex = new Mutex(true, "SteadyFocalPoint_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("Steady is already running. Check the system tray.",
                "Steady", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _settingsService = new SettingsService();
        _settingsService.Load();

        _powerMonitor = new PowerMonitorService();
        _powerMonitor.PowerStateChanged += (_, _) => ApplyPowerMode();

        _sensorManager = new SensorManager(_settingsService);
        _trayService = new TrayService();
        _hotkeyService = new HotkeyService();

        _overlay = new OverlayWindow();
        _overlay.ApplySettings(_settingsService.Current);

        _settingsWindow = new SettingsWindow(_settingsService);
        _settingsWindow.SettingsChanged += OnSettingsChanged;

        // Hidden window must be shown once for hotkey registration to get a handle
        _messageWindow = new Window
        {
            Width = 0, Height = 0, Opacity = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            AllowsTransparency = true
        };
        _messageWindow.Show();
        _messageWindow.Hide();

        _hotkeyService.Register(_messageWindow);
        _hotkeyService.HotkeyPressed += (_, _) => ToggleSteady();

        _trayService.ToggleRequested += (_, _) => ToggleSteady();
        _trayService.SettingsRequested += (_, _) => OpenSettings();
        _trayService.ExitRequested += (_, _) => ExitApp();

        _sensorManager.MotionUpdated += (_, motion) =>
            _overlay.Dispatcher.BeginInvoke(() => _overlay.UpdateMotion(motion));

        _sensorManager.TierChanged += (_, tier) =>
        {
            _overlay.Dispatcher.BeginInvoke(() => _settingsWindow.UpdateActiveTierLabel(tier));
            if (tier == ActiveSensorTier.None && _settingsService.Current.IsEnabled)
                _trayService.ShowBalloon("Steady", "No sensor found. Running in visual-only mode.");
        };

        _sensorManager.AutoActivationChanged += (_, activate) =>
            Dispatcher.BeginInvoke(async () =>
            {
                if (!_settingsService.Current.AutoActivation) return;
                if (activate && !_settingsService.Current.IsEnabled)
                {
                    await StartSteadyAsync();
                    _trayService.ShowBalloon("Steady", "Motion detected — overlay enabled automatically.");
                }
                else if (!activate && _settingsService.Current.IsEnabled)
                {
                    await StopSteadyAsync();
                }
            });

        // Launch behaviour:
        //  • Double-click / manual launch  → start running immediately (enable).
        //  • Launched at Windows startup (registry passes --startup) → stay quiet
        //    in the tray and resume the last saved state (avoids camera-on-at-boot).
        bool launchedAtStartup = Array.Exists(e.Args,
            a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        bool enable = launchedAtStartup ? _settingsService.Current.IsEnabled : true;

        if (enable)
        {
            await StartSteadyAsync();
        }
        else
        {
            _trayService.UpdateState(false);
            if (_settingsService.Current.AutoActivation)
                await _sensorManager.InitializeAsync(allowCamera: false);
            ApplyPowerMode();
        }
    }

    private async void ToggleSteady()
    {
        if (_settingsService.Current.IsEnabled)
            await StopSteadyAsync();
        else
            await StartSteadyAsync();
    }

    private async Task StartSteadyAsync()
    {
        _settingsService.Current.IsEnabled = true;
        // Now that we're enabled, start the full sensor stack (camera allowed).
        await _sensorManager.InitializeAsync(allowCamera: true);
        ApplyPowerMode();
        _overlay.ApplySettings(_settingsService.Current);
        _overlay.SetVisible(true);
        _trayService.UpdateState(true);
    }

    private async Task StopSteadyAsync()
    {
        _settingsService.Current.IsEnabled = false;
        _overlay.SetVisible(false);
        _trayService.UpdateState(false);

        if (_settingsService.Current.AutoActivation)
        {
            // Keep watching for motion to auto-re-enable, but release the camera.
            await _sensorManager.InitializeAsync(allowCamera: false);
            ApplyPowerMode();
        }
        else
        {
            // Fully release all sensors (incl. camera) while disabled.
            await _sensorManager.StopAsync();
        }
    }

    private void OpenSettings()
    {
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _overlay.ApplySettings(_settingsService.Current);
        if (!_settingsService.Current.AutoActivation)
            _sensorManager.ResetAutoActivation();
        _sensorManager.ApplySettings();
        ApplyPowerMode();
    }

    private void ApplyPowerMode()
    {
        bool saver = _settingsService.Current.BatterySaverEnabled && _powerMonitor.IsOnBattery;
        _overlay.SetBatterySaverMode(saver);
        _sensorManager.SetBatterySaverMode(saver);
    }

    private async void ExitApp()
    {
        _settingsService.Save();
        await _sensorManager.StopAsync();
        _sensorManager.Dispose();
        _hotkeyService.Dispose();
        _trayService.Dispose();
        _powerMonitor.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
