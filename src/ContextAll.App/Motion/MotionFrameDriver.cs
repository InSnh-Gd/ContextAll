using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;

namespace ContextAll.App.Motion;

/// <summary>
/// Drives vsync-aligned frame callbacks via <see cref="TopLevel.RequestAnimationFrame"/>.
/// Headless / unattached owners take a single 1/60s step so tests can continue with <c>AdvanceTime</c>.
/// </summary>
public sealed class MotionFrameDriver
{
    private readonly Visual _owner;
    private readonly Func<double, bool> _onTick;
    private bool _running;
    private int _session;
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

        if (TopLevel.GetTopLevel(_owner) is not { } topLevel)
        {
            // Headless or unattached: let caller observe immediate step completion cleanly.
            _onTick(1.0 / 60.0);
            return;
        }

        _running = true;
        _session++;
        var session = _session;
        _lastTickTimestamp = Stopwatch.GetTimestamp();
        topLevel.RequestAnimationFrame(OnFrame);
        return;

        void OnFrame(TimeSpan _)
        {
            if (!_running || session != _session)
            {
                return;
            }

            if (TopLevel.GetTopLevel(_owner) is not { } current)
            {
                Stop();
                return;
            }

            var now = Stopwatch.GetTimestamp();
            var dt = ComputeDeltaSeconds(Stopwatch.GetElapsedTime(_lastTickTimestamp, now));
            _lastTickTimestamp = now;

            var keepGoing = dt <= 0.0 || _onTick(dt);
            if (keepGoing && _running && session == _session)
            {
                current.RequestAnimationFrame(OnFrame);
            }
            else
            {
                Stop();
            }
        }
    }

    public void Stop()
    {
        _running = false;
        _session++;
    }
}
