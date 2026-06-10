using Steady.Models;

namespace Steady.Services;

public sealed class SensorManager : IDisposable
{
    private ISensorService? _activeSensor;
    private readonly SettingsService _settings;

    public ActiveSensorTier ActiveTier { get; private set; } = ActiveSensorTier.None;
    public bool IsRunning => _activeSensor != null;
    public event EventHandler<MotionVector>? MotionUpdated;
    public event EventHandler<ActiveSensorTier>? TierChanged;

    // Auto-activation: true = should start, false = should stop
    public event EventHandler<bool>? AutoActivationChanged;

    private static readonly TimeSpan ActivationDelay   = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DeactivationDelay = TimeSpan.FromSeconds(10);

    // Accessed only from sensor callback thread — no lock needed
    private DateTime? _motionStartTime;
    private DateTime? _stillStartTime;
    private bool _autoActive;

    // Adaptive threshold calibration (EMA + variance, ~15 s to stabilise)
    private const double EmaAlpha = 0.015;
    private const int CalibMinSamples = 450;
    private double _emaMag;
    private double _emaVariance;
    private int _calibSamples;
    public bool IsAdaptiveCalibrated { get; private set; }
    public double AdaptiveThresholdValue { get; private set; }

    public SensorManager(SettingsService settings)
    {
        _settings = settings;
    }

    public async Task InitializeAsync(bool allowCamera = true)
    {
        var pref = _settings.Current.PreferredTier;
        // Camera-based tiers run only when explicitly allowed (i.e. app enabled).
        var cameraEnabled = _settings.Current.CameraEnabled && allowCamera;

        if (_activeSensor != null)
        {
            _activeSensor.MotionUpdated -= OnMotionUpdated;
            await _activeSensor.StopAsync();
            _activeSensor.Dispose();
            _activeSensor = null;
            ActiveTier = ActiveSensorTier.None;
        }

        if (pref == SensorTierPreference.Auto || pref == SensorTierPreference.Gyro)
        {
            var gyro = new GyroSensorService();
            await gyro.StartAsync();
            if (gyro.IsAvailable)
            {
                SetSensor(gyro);
                return;
            }
            gyro.Dispose();
        }

        if (cameraEnabled && (pref == SensorTierPreference.Auto || pref == SensorTierPreference.OpticalFlow))
        {
            var flow = new OpticalFlowService();
            await flow.StartAsync();
            if (flow.IsAvailable)
            {
                SetSensor(flow);
                return;
            }
            flow.Dispose();
        }

        if (cameraEnabled && (pref == SensorTierPreference.Auto || pref == SensorTierPreference.Camera))
        {
            var cam = new CameraHeadTrackingService();
            await cam.StartAsync();
            if (cam.IsAvailable)
            {
                SetSensor(cam);
                return;
            }
            cam.Dispose();
        }

        if (pref == SensorTierPreference.Auto || pref == SensorTierPreference.Mic)
        {
            var mic = new MicSensorService();
            await mic.StartAsync();
            if (mic.IsAvailable)
            {
                SetSensor(mic);
                return;
            }
            mic.Dispose();
        }

        ActiveTier = ActiveSensorTier.None;
        TierChanged?.Invoke(this, ActiveTier);
    }

    private void SetSensor(ISensorService sensor)
    {
        _activeSensor = sensor;
        _activeSensor.MotionUpdated += OnMotionUpdated;
        ActiveTier = sensor.Tier;
        ApplySettings();
        TierChanged?.Invoke(this, ActiveTier);
    }

    private void OnMotionUpdated(object? sender, MotionVector motion)
    {
        MotionUpdated?.Invoke(this, motion);
        UpdateAdaptiveCalibration(motion);
        CheckAutoActivation(motion);
    }

    private void UpdateAdaptiveCalibration(MotionVector motion)
    {
        if (!_settings.Current.AdaptiveThreshold) return;

        double mag = Math.Sqrt(motion.X * motion.X + motion.Y * motion.Y);
        if (_calibSamples == 0)
        {
            _emaMag = mag;
            _emaVariance = 0;
        }
        else
        {
            double prev = _emaMag;
            _emaMag += EmaAlpha * (mag - _emaMag);
            // Online EMA variance (biased but good for our purposes)
            double delta = mag - prev;
            _emaVariance += EmaAlpha * (delta * (mag - _emaMag) - _emaVariance);
        }
        _calibSamples++;

        if (_calibSamples >= CalibMinSamples)
        {
            IsAdaptiveCalibrated = true;
            // threshold = mean + 2.5σ  (covers 99% of still-device noise)
            AdaptiveThresholdValue = _emaMag + 2.5 * Math.Sqrt(Math.Abs(_emaVariance));
        }
    }

    public void ResetAdaptiveCalibration()
    {
        _calibSamples = 0;
        _emaMag = 0;
        _emaVariance = 0;
        IsAdaptiveCalibrated = false;
        AdaptiveThresholdValue = 0;
    }

    private void CheckAutoActivation(MotionVector motion)
    {
        if (!_settings.Current.AutoActivation) return;

        double xyMag = Math.Sqrt(motion.X * motion.X + motion.Y * motion.Y);
        double threshold = (_settings.Current.AdaptiveThreshold && IsAdaptiveCalibrated)
            ? AdaptiveThresholdValue
            : _settings.Current.AutoActivationThreshold;
        bool isMoving = xyMag > threshold;
        var now = DateTime.UtcNow;

        if (isMoving)
        {
            _stillStartTime = null;
            _motionStartTime ??= now;

            if (!_autoActive && (now - _motionStartTime.Value) >= ActivationDelay)
            {
                _autoActive = true;
                AutoActivationChanged?.Invoke(this, true);
            }
        }
        else
        {
            _motionStartTime = null;
            _stillStartTime ??= now;

            if (_autoActive && (now - _stillStartTime.Value) >= DeactivationDelay)
            {
                _autoActive = false;
                AutoActivationChanged?.Invoke(this, false);
            }
        }
    }

    public void SetBatterySaverMode(bool saver)
    {
        if (_activeSensor is GyroSensorService gyro)
            gyro.SetReportInterval(saver ? 100u : 33u);
        else if (_activeSensor is OpticalFlowService flow)
            flow.SetBatterySaverMode(saver);
        else if (_activeSensor is CameraHeadTrackingService cam)
            cam.SetBatterySaverMode(saver);
        else if (_activeSensor is MicSensorService mic)
            mic.SetBatterySaverMode(saver);
    }

    public void ApplySettings()
    {
        var s = _settings.Current;
        if (_activeSensor is OpticalFlowService flow)
            flow.LowLightEnhancement = s.LowLightEnhancement;
        else if (_activeSensor is CameraHeadTrackingService cam)
            cam.LowLightEnhancement = s.LowLightEnhancement;

        if (!s.AdaptiveThreshold)
            ResetAdaptiveCalibration();
    }

    public void ResetAutoActivation()
    {
        _motionStartTime = null;
        _stillStartTime = null;
        _autoActive = false;
    }

    public async Task StopAsync()
    {
        if (_activeSensor != null)
        {
            _activeSensor.MotionUpdated -= OnMotionUpdated;
            await _activeSensor.StopAsync();
        }
    }

    public void Dispose()
    {
        if (_activeSensor != null)
        {
            _activeSensor.MotionUpdated -= OnMotionUpdated;
            _activeSensor.Dispose();
        }
    }
}
