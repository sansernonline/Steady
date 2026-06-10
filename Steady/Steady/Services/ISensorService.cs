using Steady.Models;

namespace Steady.Services;

public interface ISensorService : IDisposable
{
    bool IsAvailable { get; }
    ActiveSensorTier Tier { get; }
    event EventHandler<MotionVector>? MotionUpdated;
    Task StartAsync();
    Task StopAsync();
}
