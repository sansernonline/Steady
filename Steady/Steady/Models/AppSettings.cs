namespace Steady.Models;

public sealed class AppSettings
{
    public bool IsEnabled { get; set; } = false;
    public bool RunAtStartup { get; set; } = false;
    public int DotCount { get; set; } = 12; // legacy; layout now uses DotSpacing
    public double DotSpacing { get; set; } = 300.0; // px between dots along an edge (density-based layout)
    public double DotSize { get; set; } = 10.0;
    public string DotColor { get; set; } = "#FFFFFF";
    public double DotOpacity { get; set; } = 0.60;
    public double IntensityMultiplier { get; set; } = 1.0;
    public double MaxDotOffset { get; set; } = 50.0;
    public bool CameraEnabled { get; set; } = true;
    public SensorTierPreference PreferredTier { get; set; } = SensorTierPreference.Auto;
    public bool AdaptiveContrast { get; set; } = true;

    public bool AutoActivation { get; set; } = false;
    // XY-plane magnitude (g-units for gyro, normalized for camera) above which motion is detected
    public double AutoActivationThreshold { get; set; } = 0.05;

    // v2.0 features
    public bool BatterySaverEnabled { get; set; } = true;
    public bool LowLightEnhancement { get; set; } = true;
    public bool AdaptiveThreshold { get; set; } = true;
}

public enum SensorTierPreference { Auto, Gyro, OpticalFlow, Camera, Mic }
public enum ActiveSensorTier { None, Gyro, OpticalFlow, Camera, Mic }
