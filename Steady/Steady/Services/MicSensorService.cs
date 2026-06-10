using System.Diagnostics;
using NAudio.Wave;
using Steady.Models;

namespace Steady.Services;

/// <summary>
/// Tier 3 sensor: infers vehicle motion from microphone audio level.
/// Road noise / engine vibration increases with speed; RMS of the captured
/// signal is used as a velocity proxy when no IMU or camera is available.
/// </summary>
public sealed class MicSensorService : ISensorService
{
    private WaveInEvent? _waveIn;
    private MotionVector _smoothed = MotionVector.Zero;
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    // 16-bit Int16 max value for normalisation
    private const float MaxInt16 = 32768f;

    // RMS below this is treated as silence (background hiss, not road noise)
    private const float NoiseFloor = 0.008f;

    // RMS above this maps to full intensity (very loud cabin / highway noise)
    private const float SaturationLevel = 0.08f;

    private const double LerpFactor = 0.08; // slow, bus-smooth motion
    private volatile int _skipMod = 1; // 1 = every buffer, 2 = every other buffer
    private int _bufferIndex;

    public void SetBatterySaverMode(bool saver) => _skipMod = saver ? 2 : 1;

    public bool IsAvailable { get; private set; }
    public ActiveSensorTier Tier => ActiveSensorTier.Mic;
    public event EventHandler<MotionVector>? MotionUpdated;

    public Task StartAsync()
    {
        try
        {
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(22050, 1), // mono 22 kHz — enough for noise detection
                BufferMilliseconds = 80
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            IsAvailable = true;
        }
        catch
        {
            IsAvailable = false;
        }
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (++_bufferIndex % _skipMod != 0) return;

        // Compute RMS of Int16 PCM samples
        int count = e.BytesRecorded / 2;
        if (count == 0) return;

        float sumSq = 0;
        for (int i = 0; i < e.BytesRecorded - 1; i += 2)
        {
            float s = BitConverter.ToInt16(e.Buffer, i) / MaxInt16;
            sumSq += s * s;
        }
        float rms = MathF.Sqrt(sumSq / count);

        // Subtract noise floor; map remaining signal to [0, 1]
        float above = Math.Max(0f, rms - NoiseFloor);
        float speed = Math.Clamp(above / (SaturationLevel - NoiseFloor), 0f, 1f);

        // Road noise gives no direction — emit as a slow forward drift (Y) with
        // a gentle oscillating shimmer (X) so the dots don't appear frozen.
        double t = _sw.Elapsed.TotalSeconds;
        double nx = speed * Math.Sin(t * 0.35) * 0.45;
        double ny = speed * 0.70;

        _smoothed = _smoothed.Lerp(new MotionVector(nx, ny, 0), LerpFactor);
        MotionUpdated?.Invoke(this, _smoothed);
    }

    public Task StopAsync()
    {
        _waveIn?.StopRecording();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _waveIn?.Dispose();
        IsAvailable = false;
    }
}
