using System.IO;
using System.Text.Json;
using Steady.Models;

namespace Steady.Services;

public sealed class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Steady", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var json = File.ReadAllText(SettingsPath);
            Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch
        {
            Current = new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch { }
    }

    public void SetStartup(bool enable)
    {
        Current.RunAtStartup = enable;

        // Registry fallback for non-MSIX; MSIX uses StartupTask API in manifest
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;

            if (enable)
                // --startup tells the app it was auto-launched at boot, so it stays
                // quiet instead of auto-enabling (manual double-click has no args).
                key.SetValue("Steady", $"\"{Environment.ProcessPath}\" --startup");
            else
                key.DeleteValue("Steady", throwOnMissingValue: false);
        }
        catch { }
    }
}
