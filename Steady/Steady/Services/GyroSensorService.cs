using Steady.Models;
using Windows.Devices.Sensors;

namespace Steady.Services;

public sealed class GyroSensorService : ISensorService
{
    private Accelerometer? _accelerometer;
    private Gyrometer? _gyrometer;
    private bool _running;

    public bool IsAvailable => _accelerometer != null;
    public ActiveSensorTier Tier => ActiveSensorTier.Gyro;

    public event EventHandler<MotionVector>? MotionUpdated;

    public Task StartAsync()
    {
        _accelerometer = Accelerometer.GetDefault();
        _gyrometer = Gyrometer.GetDefault();

        if (_accelerometer == null) return Task.CompletedTask;

        _accelerometer.ReportInterval = Math.Max(_accelerometer.MinimumReportInterval, 33u);
        _accelerometer.ReadingChanged += OnAccelerometerReadingChanged;

        if (_gyrometer != null)
        {
            _gyrometer.ReportInterval = Math.Max(_gyrometer.MinimumReportInterval, 33u);
        }

        _running = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (_accelerometer != null)
            _accelerometer.ReadingChanged -= OnAccelerometerReadingChanged;
        _running = false;
        return Task.CompletedTask;
    }

    private void OnAccelerometerReadingChanged(Accelerometer sender, AccelerometerReadingChangedEventArgs args)
    {
        if (!_running) return;
        var r = args.Reading;
        // AccelerationX/Y/Z are in g-units; negate Z (gravity bias) to get lateral/fore-aft
        MotionUpdated?.Invoke(this, new MotionVector(r.AccelerationX, r.AccelerationY, r.AccelerationZ));
    }

    public void SetReportInterval(uint milliseconds)
    {
        if (_accelerometer == null) return;
        uint clamped = Math.Max(_accelerometer.MinimumReportInterval, milliseconds);
        _accelerometer.ReportInterval = clamped;
        if (_gyrometer != null)
            _gyrometer.ReportInterval = clamped;
    }

    public void Dispose()
    {
        if (_accelerometer != null)
            _accelerometer.ReadingChanged -= OnAccelerometerReadingChanged;
    }
}
