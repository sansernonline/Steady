using System.IO;
using OpenCvSharp;
using Steady.Models;
using Size = OpenCvSharp.Size;

namespace Steady.Services;

/// <summary>
/// Tier 2 sensor: detects head movement via webcam to infer vehicle motion.
/// Requires haarcascade_frontalface_default.xml in Assets folder or CascadePath.
/// Download from: https://github.com/opencv/opencv/blob/master/data/haarcascades/haarcascade_frontalface_default.xml
/// </summary>
public sealed class CameraHeadTrackingService : ISensorService
{
    public static readonly string CascadePath = Path.Combine(
        AppContext.BaseDirectory, "Assets", "haarcascade_frontalface_default.xml");

    private VideoCapture? _capture;
    private CascadeClassifier? _cascade;
    private CancellationTokenSource? _cts;
    private Task? _trackingTask;

    private Point2d _baseline;
    private bool _baselineSet;
    private MotionVector _smoothed = MotionVector.Zero;

    // Low-pass filter factor (higher = more responsive, lower = smoother)
    private const double LerpFactor = 0.15;
    private const int TargetFps = 15;
    private const double NormalizationScale = 80.0; // pixels → normalized [-1,1]

    // Battery: only run at full FPS while there is motion; drop to a few FPS when
    // still, search a small ROI instead of the whole frame, and enhance contrast
    // only when the frame is actually dark.
    private const double IdleMotionThreshold = 0.04; // |motion| below this counts as "still"
    private const double IdleAfterSeconds   = 2.5;   // go idle after this long with no motion
    private const int RoiMissesBeforeFullScan = 5;   // ROI misses before a full-frame rescan
    private const double DarkMeanThreshold  = 90.0;  // apply CLAHE only when mean brightness below this
    private const double DeepIdleAfterSeconds = 8.0; // fully release the camera after this long with no motion
    private const int WakeSampleFrames = 8;          // frames sampled on each wake to test for motion
    private const double WakeMovePx = 6.0;           // total head movement (px) during sample → resume active

    public bool IsAvailable { get; private set; }
    public ActiveSensorTier Tier => ActiveSensorTier.Camera;

    public volatile bool LowLightEnhancement = true;
    private volatile bool _batterySaver;

    // ROI tracking state — limits expensive full-frame face detection.
    private Rect _lastFace;
    private bool _haveFace;
    private int _roiMisses;
    private DateTime _lastMotionUtc = DateTime.UtcNow;

    public void SetBatterySaverMode(bool saver) => _batterySaver = saver;

    private int ActiveDelayMs   => _batterySaver ? 100 : 1000 / TargetFps; // 10 vs 15 fps
    private int IdleDelayMs     => _batterySaver ? 500 : 250;              // 2 vs 4 fps when still
    private int DeepIdlePollMs  => _batterySaver ? 6000 : 4000;            // how often to wake & check while camera is off

    public event EventHandler<MotionVector>? MotionUpdated;

    public Task StartAsync()
    {
        if (!File.Exists(CascadePath))
        {
            IsAvailable = false;
            return Task.CompletedTask;
        }

        _cascade = new CascadeClassifier(CascadePath);

        if (!TryOpenCapture())
        {
            IsAvailable = false;
            return Task.CompletedTask;
        }

        IsAvailable = true;
        _baselineSet = false;
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
        using var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, new Size(8, 8));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // DEEP IDLE: camera fully released. Wake briefly every poll interval
                // to check whether motion resumed; otherwise power it back down.
                if (_capture == null)
                {
                    if (ct.WaitHandle.WaitOne(DeepIdlePollMs)) break;
                    if (!TryOpenCapture()) continue;

                    if (DetectWakeMotion(frame, gray, clahe, ct))
                    {
                        _lastMotionUtc = DateTime.UtcNow; // motion is back → resume active
                    }
                    else
                    {
                        ReleaseCapture(); // still still → power the camera sensor back down
                        continue;
                    }
                }

                // ACTIVE: process one frame.
                if (_capture == null || !_capture.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(ActiveDelayMs);
                    continue;
                }
                ProcessFrame(frame, gray, clahe);

                double idleSec = (DateTime.UtcNow - _lastMotionUtc).TotalSeconds;
                if (idleSec >= DeepIdleAfterSeconds)
                {
                    // Long still period → fully release the camera (real power saving).
                    ReleaseCapture();
                    _haveFace = false;
                    _baselineSet = false; // re-establish baseline on resume to avoid a jump
                    continue;
                }

                // Full FPS while motion is recent; otherwise drop to a few FPS.
                Thread.Sleep(idleSec < IdleAfterSeconds ? ActiveDelayMs : IdleDelayMs);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                Thread.Sleep(ActiveDelayMs * 2);
            }
        }

        ReleaseCapture();
    }

    // Reads one frame, detects head movement, and emits a motion vector.
    private void ProcessFrame(Mat frame, Mat gray, CLAHE clahe)
    {
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

        // Contrast enhancement only when the frame is actually dark — skips a
        // per-frame histogram pass in normal daylight to save CPU/battery.
        if (LowLightEnhancement && Cv2.Mean(gray).Val0 < DarkMeanThreshold)
            clahe.Apply(gray, gray);

        var face = DetectFace(gray);
        if (face is { } f)
        {
            var center = new Point2d(f.X + f.Width / 2.0, f.Y + f.Height / 2.0);
            if (!_baselineSet)
            {
                _baseline = center;
                _baselineSet = true;
            }
            else
            {
                double dx = Math.Clamp((center.X - _baseline.X) / NormalizationScale, -1.0, 1.0);
                double dy = Math.Clamp((center.Y - _baseline.Y) / NormalizationScale, -1.0, 1.0);

                var raw = new MotionVector(dx, -dy, 0); // invert Y: face down = accel forward
                _smoothed = _smoothed.Lerp(raw, LerpFactor);
                MotionUpdated?.Invoke(this, _smoothed);

                if (_smoothed.Magnitude > IdleMotionThreshold)
                    _lastMotionUtc = DateTime.UtcNow;
            }
        }
        else if (_baselineSet)
        {
            // No face: interpolate back toward zero
            _smoothed = _smoothed.Lerp(MotionVector.Zero, LerpFactor * 0.5);
            MotionUpdated?.Invoke(this, _smoothed);
        }
    }

    // Opens the webcam at low resolution. Returns false if no camera is available.
    private bool TryOpenCapture()
    {
        var cap = new VideoCapture(0);
        if (!cap.IsOpened())
        {
            cap.Dispose();
            return false;
        }
        cap.Set(VideoCaptureProperties.FrameWidth, 320);
        cap.Set(VideoCaptureProperties.FrameHeight, 240);
        cap.Set(VideoCaptureProperties.Fps, TargetFps);
        _capture = cap;
        return true;
    }

    // Fully powers down the camera sensor (not just lowering FPS).
    private void ReleaseCapture()
    {
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
    }

    // Samples a few frames right after a wake to decide whether motion has resumed.
    // Uses frame-to-frame head movement so it works without a fresh baseline.
    private bool DetectWakeMotion(Mat frame, Mat gray, CLAHE clahe, CancellationToken ct)
    {
        Point2d? prev = null;
        double moved = 0;

        for (int i = 0; i < WakeSampleFrames && !ct.IsCancellationRequested; i++)
        {
            if (_capture == null || !_capture.Read(frame) || frame.Empty())
            {
                Thread.Sleep(ActiveDelayMs);
                continue;
            }

            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            if (LowLightEnhancement && Cv2.Mean(gray).Val0 < DarkMeanThreshold)
                clahe.Apply(gray, gray);

            var face = DetectFace(gray);
            if (face is { } f)
            {
                var c = new Point2d(f.X + f.Width / 2.0, f.Y + f.Height / 2.0);
                if (prev is { } p)
                    moved += Math.Abs(c.X - p.X) + Math.Abs(c.Y - p.Y);
                prev = c;
            }

            Thread.Sleep(ActiveDelayMs);
        }

        return moved > WakeMovePx;
    }

    // Detects the largest face. To save CPU it first searches only a region around
    // the last known face; it falls back to a full-frame scan when the face is lost
    // for several frames (or on the very first detection).
    private Rect? DetectFace(Mat gray)
    {
        if (_haveFace)
        {
            int pad = (int)(Math.Max(_lastFace.Width, _lastFace.Height) * 0.6);
            int rx = Math.Max(0, _lastFace.X - pad);
            int ry = Math.Max(0, _lastFace.Y - pad);
            int rw = Math.Min(gray.Width - rx, _lastFace.Width + 2 * pad);
            int rh = Math.Min(gray.Height - ry, _lastFace.Height + 2 * pad);

            if (rw > 0 && rh > 0)
            {
                using var sub = new Mat(gray, new Rect(rx, ry, rw, rh));
                var hits = _cascade!.DetectMultiScale(sub, 1.1, 4, HaarDetectionTypes.ScaleImage, new Size(40, 40));
                if (hits.Length > 0)
                {
                    var best = hits.OrderByDescending(r => r.Width * r.Height).First();
                    best.X += rx;
                    best.Y += ry;
                    _lastFace = best;
                    _roiMisses = 0;
                    return best;
                }
                if (++_roiMisses < RoiMissesBeforeFullScan)
                    return null; // skip the costly full-frame scan for a few frames
            }
        }

        // Full-frame scan (first detection or after repeated ROI misses)
        var faces = _cascade!.DetectMultiScale(gray, 1.1, 4, HaarDetectionTypes.ScaleImage, new Size(40, 40));
        if (faces.Length > 0)
        {
            var best = faces.OrderByDescending(r => r.Width * r.Height).First();
            _lastFace = best;
            _haveFace = true;
            _roiMisses = 0;
            return best;
        }

        _haveFace = false;
        return null;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _capture?.Dispose();
        _cascade?.Dispose();
    }
}
