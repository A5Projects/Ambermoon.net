using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Ambermoon;

/// <summary>
/// Opt-in frame pacing diagnostics (--frame-stats). Records the time between
/// consecutive render and update calls, swap time, pacer wait time, and the
/// real OpenGL swap interval, and writes a summary on exit.
/// </summary>
sealed class FrameStats
{
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly List<(double Time, float Value)> renderIntervals = new(1 << 16);
    readonly List<(double Time, float Value)> updateIntervals = new(1 << 16);
    readonly List<(double Time, float Value)> swapDurations = new(1 << 16);
    readonly List<(double Time, float Value)> waitDurations = new(1 << 16);
    readonly List<(double Time, float Value)> updatesPerRender = new(1 << 16);
    readonly string logPath;
    readonly string description;
    double lastRender = -1;
    double lastUpdate = -1;
    double steadyStart = -1;
    int updatesSinceRender;
    double nextReport = 5.0;
    int? swapIntervalAtStart;
    bool written;

    public FrameStats(string logPath, string description)
    {
        this.logPath = logPath;
        this.description = description;
    }

    public void SetSwapIntervalAtStart(int? interval) => swapIntervalAtStart = interval;

    public void MarkGameplayActive()
    {
        if (steadyStart < 0)
            steadyStart = clock.Elapsed.TotalSeconds + 1.5;
    }

    public void OnUpdate()
    {
        double now = clock.Elapsed.TotalSeconds;
        if (lastUpdate >= 0)
            updateIntervals.Add((now, (float)((now - lastUpdate) * 1000.0)));
        lastUpdate = now;
        ++updatesSinceRender;
    }

    public void OnRender(double swapMs, double previousWaitMs, Func<string> liveInfo)
    {
        double now = clock.Elapsed.TotalSeconds;
        if (lastRender >= 0)
        {
            renderIntervals.Add((now, (float)((now - lastRender) * 1000.0)));
            swapDurations.Add((now, (float)swapMs));
            waitDurations.Add((now, (float)previousWaitMs));
            updatesPerRender.Add((now, updatesSinceRender));
        }
        lastRender = now;
        updatesSinceRender = 0;

        if (now >= nextReport)
        {
            nextReport += 5.0;
            Console.WriteLine("[frame-stats] " + Summarize(liveInfo?.Invoke(), recentOnly: true));
        }
    }

    float[] SelectValues(List<(double Time, float Value)> series, bool recentOnly)
    {
        const int Window = 600;
        if (series.Count == 0)
            return [];
        if (recentOnly)
        {
            int skip = Math.Max(0, series.Count - Window);
            return series.Skip(skip).Select(s => s.Value).ToArray();
        }
        double cutoff = steadyStart > 0 ? steadyStart : Math.Min(2.0, clock.Elapsed.TotalSeconds * 0.25);
        var steady = series.Where(s => s.Time >= cutoff).Select(s => s.Value).ToArray();
        return steady.Length >= 10 ? steady : series.Select(s => s.Value).ToArray();
    }

    static string Describe(float[] values)
    {
        if (values.Length < 2)
            return "n/a";

        var sorted = values.OrderBy(v => v).ToArray();
        double mean = sorted.Average();
        double stdDev = Math.Sqrt(sorted.Sum(v => (v - mean) * (v - mean)) / sorted.Length);
        float P(double p) => sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];
        return $"mean {mean:F3} ms ({1000.0 / Math.Max(0.0001, mean):F1}/s), median {P(0.5):F3}, p1 {P(0.01):F3}, p99 {P(0.99):F3}, " +
               $"max {sorted[^1]:F3}, stddev {stdDev:F3}";
    }

    static string DescribeDuration(float[] values)
    {
        if (values.Length < 2)
            return "n/a";
        var sorted = values.OrderBy(v => v).ToArray();
        float P(double p) => sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];
        return $"mean {sorted.Average():F2} ms, median {P(0.5):F2}, p99 {P(0.99):F2}";
    }

    string Summarize(string live, bool recentOnly)
    {
        var sb = new StringBuilder();
        sb.Append("render: ").Append(Describe(SelectValues(renderIntervals, recentOnly)));
        sb.Append(" | update: ").Append(Describe(SelectValues(updateIntervals, recentOnly)));
        sb.Append(" | swap: ").Append(DescribeDuration(SelectValues(swapDurations, recentOnly)));
        sb.Append(" | wait: ").Append(DescribeDuration(SelectValues(waitDurations, recentOnly)));
        var upr = SelectValues(updatesPerRender, recentOnly);
        if (upr.Length > 0)
            sb.Append($" | updates/render avg {upr.Average():F2}, zero {upr.Count(u => u == 0) * 100.0 / upr.Length:F1}%, 2+ {upr.Count(u => u >= 2) * 100.0 / upr.Length:F1}%");
        if (!string.IsNullOrEmpty(live))
            sb.Append(" | ").Append(live);
        return sb.ToString();
    }

    public void Write(Func<string> liveInfo)
    {
        if (written)
            return;
        written = true;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Ambermoon frame stats {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Settings: {description}");
            sb.AppendLine($"WGL swap interval at start: {swapIntervalAtStart?.ToString() ?? "unknown"}");
            sb.AppendLine($"Frames: {renderIntervals.Count + 1}, updates: {updateIntervals.Count + 1}, duration {clock.Elapsed.TotalSeconds:F1} s");
            sb.AppendLine(Summarize(liveInfo?.Invoke(), recentOnly: false).Replace(" | ", Environment.NewLine));
            sb.AppendLine();
            File.AppendAllText(logPath, sb.ToString());
            Console.WriteLine("[frame-stats] summary written to " + logPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[frame-stats] could not write log: " + ex.Message);
        }
    }
}
