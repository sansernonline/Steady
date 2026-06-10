using System.Windows;
using System.Windows.Interop;
using Steady.Helpers;

namespace Steady.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 9001;
    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public void Register(Window messageWindow)
    {
        var helper = new WindowInteropHelper(messageWindow);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source.AddHook(WndProc);

        _registered = Win32Helper.RegisterHotKey(
            helper.Handle, HotkeyId,
            Win32Helper.MOD_CONTROL | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT,
            Win32Helper.VK_M);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Helper.WM_HOTKEY && (int)wParam == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered && _source != null)
            Win32Helper.UnregisterHotKey(_source.Handle, HotkeyId);
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
    }
}
