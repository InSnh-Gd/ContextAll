using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ContextAll.App.Motion;

/// <summary>Drives paced ~60fps frame callbacks using <see cref="DispatcherTimer"/> and real wall-clock elapsed time.</summary>
public sealed class MotionFrameDriver
{
    private readonly Visual _owner;
    private readonly Func<double, bool> _onTick;
    private DispatcherTimer? _timer;
    private bool _running;
    private long _lastTickTimestamp;

    public MotionFrameDriver(Visual owner, Func<double, bool> onTick)
    {
        _owner = owner;
        _onTick = onTick;
    }

    public bool IsRunning => _running;

    public static double ComputeDeltaSeconds(TimeSpan elapsed) =>
        Math.Clamp(double.IsFinite(elapsed.TotalSeconds) ? elapsed.TotalSeconds : 0.0, 0.0, 0.05);

    public void Start()
    {
        if (_running)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(_owner);
        if (topLevel is null)
        {
            // Headless or unattached: let caller observe immediate step completion cleanly.
            _onTick(1.0 / 60.0);
            return;
        }

        _running = true;
        _lastTickTimestamp = Stopwatch.GetTimestamp();
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnTimerTick);
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer?.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_running || TopLevel.GetTopLevel(_owner) is null)
        {
            Stop();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var dt = ComputeDeltaSeconds(Stopwatch.GetElapsedTime(_lastTickTimestamp, now));
        _lastTickTimestamp = now;

        if (dt <= 0.0)
        {
            return;
        }

        if (!_onTick(dt))
        {
            Stop();
        }
    }
}
