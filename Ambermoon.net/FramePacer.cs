using Silk.NET.Core.Contexts;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ambermoon;

/// <summary>
/// Precise desktop frame pacer and VSync safeguard.
/// Prevents runaway framerates (e.g. 1500+ fps when a GPU driver overrides VSync to off
/// while Silk.NET skips its own limiter because VSync is marked enabled) and replaces
/// Silk.NET's 100%-CPU busy-spin cap with a hybrid sleep + short spin-wait.
/// </summary>
sealed class FramePacer : IDisposable
{
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    static extern uint TimeEndPeriod(uint period);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate int GetSwapIntervalDelegate();

    const int BypassTriggerStreak = 8;
    const double SleepThresholdSeconds = 0.0025;
    const double SpinMarginSeconds = 0.0015;
    const double WindowedUnlimitedSafeCap = 360.0;

    readonly bool timerPeriodActive;
    double nextDeadline = double.NaN;
    int fastSwapStreak;
    IGLContext cachedContext;
    GetSwapIntervalDelegate cachedGetSwapInterval;
    double lastSwapQueryTime = -10.0;
    int? cachedSwapInterval;

    public bool VSyncRequested { get; private set; } = true;
    /// <summary>
    /// &gt; 0: explicit FPS cap. 0: auto (VSync when blocking, or safe 240 fps ceiling when VSync is off).
    /// -2: 2x display refresh rate. -1: explicitly uncapped in exclusive fullscreen (DWM-safe cap in Windows windowed/borderless).
    /// </summary>
    public double RequestedMaxFps { get; private set; }
    public int MonitorRefreshRate { get; private set; } = 60;
    public bool IsExclusiveFullscreen { get; private set; }
    public bool VSyncBypassDetected { get; private set; }
    public double LastWaitMs { get; private set; }

    public double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    public int EffectiveRefreshRate => Math.Clamp(MonitorRefreshRate > 0 ? MonitorRefreshRate : 120, 30, 360);

    public double EffectiveFpsCap
    {
        get
        {
            if (RequestedMaxFps == -1)
                return OperatingSystem.IsWindows() && !IsExclusiveFullscreen ? WindowedUnlimitedSafeCap : 0;
            if (RequestedMaxFps == -2)
                return Math.Clamp(EffectiveRefreshRate * 2, 60, 360);
            if (RequestedMaxFps > 0)
                return RequestedMaxFps;
            if (!VSyncRequested)
                return Math.Max(EffectiveRefreshRate, 240);
            if (VSyncBypassDetected)
                return EffectiveRefreshRate;
            return 0;
        }
    }

    public FramePacer()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                timerPeriodActive = TimeBeginPeriod(1) == 0;
            }
            catch
            {
                timerPeriodActive = false;
            }
        }
    }

    public void Configure(bool vsyncRequested, double requestedMaxFps, int? monitorRefreshRate)
    {
        VSyncRequested = vsyncRequested;
        RequestedMaxFps = requestedMaxFps;
        if (monitorRefreshRate is > 0)
            MonitorRefreshRate = monitorRefreshRate.Value;
        OnDisplayModeChanged();
    }

    public void UpdateMonitorRefreshRate(int? monitorRefreshRate)
    {
        if (monitorRefreshRate is > 0 && monitorRefreshRate.Value != MonitorRefreshRate)
        {
            MonitorRefreshRate = monitorRefreshRate.Value;
            OnDisplayModeChanged();
        }
    }

    public void SetExclusiveFullscreen(bool exclusiveFullscreen)
    {
        if (IsExclusiveFullscreen != exclusiveFullscreen)
        {
            IsExclusiveFullscreen = exclusiveFullscreen;
            OnDisplayModeChanged();
        }
    }

    public void OnDisplayModeChanged()
    {
        fastSwapStreak = 0;
        VSyncBypassDetected = false;
        lastSwapQueryTime = -10.0;
        ResetDeadline();
    }

    public void ResetDeadline()
    {
        nextDeadline = double.NaN;
    }

    public int? QuerySwapInterval(IGLContext context, bool force = false)
    {
        if (!OperatingSystem.IsWindows() || context == null)
            return null;

        double now = Now;
        if (!force && ReferenceEquals(context, cachedContext) && now - lastSwapQueryTime < 1.0)
            return cachedSwapInterval;

        lastSwapQueryTime = now;
        if (!ReferenceEquals(context, cachedContext))
        {
            cachedContext = context;
            cachedGetSwapInterval = null;
            try
            {
                if (context.TryGetProcAddress("wglGetSwapIntervalEXT", out nint address) && address != 0)
                    cachedGetSwapInterval = Marshal.GetDelegateForFunctionPointer<GetSwapIntervalDelegate>(address);
            }
            catch
            {
                cachedGetSwapInterval = null;
            }
        }

        try
        {
            cachedSwapInterval = cachedGetSwapInterval?.Invoke();
        }
        catch
        {
            cachedSwapInterval = null;
        }
        return cachedSwapInterval;
    }

    public void EndFrameAndWait(double frameStart, double swapStart, double swapEnd, int? swapInterval)
    {
        LastWaitMs = 0;
        double now = Now;

        if (VSyncRequested && RequestedMaxFps == 0 && !VSyncBypassDetected)
        {
            double frameMs = (swapEnd - frameStart) * 1000.0;
            double swapMs = (swapEnd - swapStart) * 1000.0;
            double expectedMs = 1000.0 / EffectiveRefreshRate;

            if (swapInterval == 0 || (frameMs < expectedMs * 0.70 && swapMs < 1.5))
            {
                if (++fastSwapStreak >= BypassTriggerStreak)
                {
                    VSyncBypassDetected = true;
                    ResetDeadline();
                }
            }
            else
            {
                fastSwapStreak = 0;
            }
        }

        double cap = EffectiveFpsCap;
        if (cap <= 0)
        {
            ResetDeadline();
            return;
        }

        double period = 1.0 / cap;
        if (double.IsNaN(nextDeadline))
        {
            nextDeadline = frameStart + period;
        }
        else
        {
            nextDeadline += period;
            if (now - nextDeadline > period)
                nextDeadline = now + period;
        }

        double waitStart = now;
        while (true)
        {
            double remaining = nextDeadline - Now;
            if (remaining <= 0)
                break;

            if (remaining > SleepThresholdSeconds)
            {
                int sleepMs = (int)((remaining - SpinMarginSeconds) * 1000.0);
                if (sleepMs >= 1)
                    Thread.Sleep(sleepMs);
                else
                    Thread.Yield();
            }
            else
            {
                Thread.SpinWait(64);
            }
        }
        LastWaitMs = (Now - waitStart) * 1000.0;
    }

    public string Describe()
    {
        double cap = EffectiveFpsCap;
        string mode = RequestedMaxFps == -1
            ? (cap > 0 ? $"uncapped (DWM-safe cap {cap:0.#} fps)" : "uncapped")
            : RequestedMaxFps == -2
                ? $"cap {cap:0.#} fps (2x refresh)"
                : cap > 0
                    ? (VSyncBypassDetected ? $"safety-cap {cap:0.#} fps (VSync bypass detected)" : $"cap {cap:0.#} fps")
                    : "VSync-driven";
        return $"pacer={mode}, refresh={MonitorRefreshRate} Hz, wglSwapInterval={cachedSwapInterval?.ToString() ?? "?"}";
    }

    public void Dispose()
    {
        if (timerPeriodActive)
        {
            try
            {
                TimeEndPeriod(1);
            }
            catch
            {
                // ignore
            }
        }
    }
}
