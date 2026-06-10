using System.Reflection;

namespace Steady.Helpers;

/// <summary>
/// Single source of truth for the app version. The number itself lives in
/// Steady.csproj (&lt;Version&gt;) — this just reads it back at runtime so the
/// UI and tray always show whatever was built.
/// </summary>
public static class AppInfo
{
    public const string Name = "Steady";

    // Semantic version MAJOR.MINOR.PATCH (e.g. 1.0.0)
    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static string Display => $"{Name} v{Version}";
}
