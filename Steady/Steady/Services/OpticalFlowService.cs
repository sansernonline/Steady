using OpenCvSharp;
using Steady.Models;
using Size = OpenCvSharp.Size;

namespace Steady.Services;

/// <summary>
/// Tier 2 sensor: detects vehicle motion via Lucas-Kanade sparse optical flow.
/// Measures camera shake / scene movement — works without a visible face.
/// </summary>
public sealed class OpticalFlowService : ISensorService
{
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _trackingTask;
    private MotionVector _smoothed = MotionVector.Zero;

    // Pixels of median inter-frame flow that map to normalized ±1
    private const double NormalizationScale = 4.0;
    private const double LerpFactor = 0.25;
    private const int TargetFps = 15;
    // Re-detect corners after this many frames, or when tracked count drops below MinPoints
    private const int RefreshEvery = 45;
    private const int MinPoints = 8;
    private const int MaxPoints = 80;

    public bool IsAvailable { get; private set; }
    public ActiveSensorTier Tier => ActiveSensorTier.OpticalFlow;
    public event EventHandler<MotionVector>? MotionUpdated;

    public volatile bool LowLightEnhancement = true;
    private volatile int _delayMs = 1000 / TargetFps;

    public void SetBatterySaverMode(bool saver)
        => _delayMs = saver ? 100 : 1000 / TargetFps;

    public Task StartAsync()
    {
        _capture = new VideoCapture(0);
        if (!_capture.IsOpened())
        {
            IsAvailable = false;
            _capture.Dispose();
            _capture = null;
            return Task.CompletedTask;
        }

        _capture.Set(VideoCaptureProperties.FrameWidth, 320);
        _capture.Set(VideoCaptureProperties.FrameHeight, 240);
        _capture.Set(VideoCaptureProperties.Fps, TargetFps);

        IsAvailable = true;
        _cts = new CancellationTokenSource();
        _trackingTask = Task.Run(() => TrackingLoop(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_trackingTask != null)
            await _trackingTask.ConfigureAwait(false);
        _capture?.Release();
        IsAvailable = false;
    }

    private void TrackingLoop(CancellationToken ct)
    {
        using var frame = new Mat();
        using var gray = new Mat();
        using var noMask = new Mat();
        using var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, new Size(8, 8));
        Mat? prevGray = null;
        Point2f[]? prevPoints = null;
        int frameCount = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_capture == null || !_capture.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(_delayMs);
                    continue;
                }

                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                if (LowLightEnhancement)
                    clahe.Apply(gray, gray);

                bool needRefresh = prevGray == null
                    || prevPoints == null
                    || prevPoints.Length < MinPoints
                    || frameCount % RefreshEvery == 0;

                if (needRefresh)
                {
                    // GoodFeaturesToTrack returns Point2f[]; mask param is required in this overload
                    prevPoints = Cv2.GoodFeaturesToTrack(gray, MaxPoints, 0.01, 10, noMask, 3, false, 0.04);
                    prevGray?.Dispose();
                    prevGray = gray.Clone();
                    frameCount = 0;
                    frameCount++;
                    Thread.Sleep(_delayMs);
                    continue;
                }

                // Lucas-Kanade sparse optical flow (Mat-based overload)
                using var prevPtsMat = Mat.FromArray(prevPoints!);
                using var nextPtsMat = new Mat();
                using var statusMat = new Mat();
                using var errMat = new Mat();

                Cv2.CalcOpticalFlowPyrLK(prevGray!, gray, prevPtsMat, nextPtsMat, statusMat, errMat);

                nextPtsMat.GetArray(out Point2f[] nextPoints);
                statusMat.GetArray(out byte[] status);

                // Collect flow for successfully tracked points
                var dxList = new List<double>(prevPoints!.Length);
                var dyList = new List<double>(prevPoints.Length);
                var goodNext = new List<Point2f>(prevPoints.Length);

                int len = Math.Min(Math.Min(prevPoints.Length, nextPoints.Length), status.Length);
                for (int i = 0; i < len; i++)
                {
                    if (status[i] == 0) continue;
                    dxList.Add(nextPoints[i].X - prevPoints[i].X);
                    dyList.Add(nextPoints[i].Y - prevPoints[i].Y);
                    goodNext.Add(nextPoints[i]);
                }

                if (dxList.Count >= MinPoints)
                {
                    double nx = Math.Clamp(Median(dxList) / NormalizationScale, -1.0, 1.0);
                    double ny = Math.Clamp(Median(dyList) / NormalizationScale, -1.0, 1.0);

                    _smoothed = _smoothed.Lerp(new MotionVector(nx, -ny, 0), LerpFactor);
                    MotionUpdated?.Invoke(this, _smoothed);
                }

                prevGray!.Dispose();
                prevGray = gray.Clone();
                prevPoints = goodNext.Count >= MinPoints ? goodNext.ToArray() : null;
                frameCount++;

                Thread.Sleep(_delayMs);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                prevPoints = null;
                Thread.Sleep(_delayMs * 2);
            }
        }

        prevGray?.Dispose();
    }

    private static double Median(List<double> values)
    {
        var copy = new List<double>(values);
        copy.Sort();
        return copy[copy.Count / 2];
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _capture?.Dispose();
    }
}
