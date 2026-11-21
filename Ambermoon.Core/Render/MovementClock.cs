using System.Diagnostics;

namespace Ambermoon.Render
{
    /// <summary>
    /// Shared monotonic clock (seconds) used to timestamp 3D movement steps and to sample
    /// the interpolated camera. Both sides must use the same clock domain.
    /// </summary>
    public static class MovementClock
    {
        public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    }
}
