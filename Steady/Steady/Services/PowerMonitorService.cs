using Microsoft.Win32;
using System.Windows.Forms;

namespace Steady.Services;

/// <summary>
/// Monitors AC/battery power state via Win32 SystemEvents and exposes IsOnBattery.
/// </summary>
public sealed class PowerMonitorService : IDisposable
{
    public bool IsOnBattery { get; private set; }

    /// <summary>Raised on the thread that owns the message loop when power state changes.</summary>
    public event EventHandler? PowerStateChanged;

    public PowerMonitorService()
    {
        IsOnBattery = ReadBatteryState();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private static bool ReadBatteryState()
        => SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online;

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.StatusChange) return;
        IsOnBattery = ReadBatteryState();
        PowerStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
        => SystemEvents.PowerModeChanged -= OnPowerModeChanged;
}
