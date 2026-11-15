using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace ContextAll.App.Motion;

/// <summary>Drives per-frame animations using TopLevel.RequestAnimationFrame while active animations remain running.</summary>
public sealed class MotionFrameDriver
{
    private readonly Visual _owner;
    private readonly Func<double, bool> _onTick;
    private bool _running;
    private TimeSpan? _lastTimestamp;

    public MotionFrameDriver(Visual owner, Func<double, bool> onTick)
    {
        _owner = owner;
        _onTick = onTick;
    }

    public bool IsRunning => _running;

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
        _lastTimestamp = null;
        ScheduleNext(topLevel);
    }

    public void Stop()
    {
        _running = false;
        _lastTimestamp = null;
    }

    private void ScheduleNext(TopLevel topLevel)
    {
        topLevel.RequestAnimationFrame(timestamp =>
        {
            if (!_running)
            {
                _lastTimestamp = null;
                return;
            }

            var currentTopLevel = TopLevel.GetTopLevel(_owner);
            if (currentTopLevel is null)
            {
                _running = false;
                _lastTimestamp = null;
                return;
            }

            var dt = _lastTimestamp is { } prev
                ? Math.Clamp((timestamp - prev).TotalSeconds, 1.0 / 240.0, 0.05)
                : 1.0 / 60.0;
            _lastTimestamp = timestamp;

            var keepGoing = _onTick(dt);
            if (!keepGoing)
            {
                _running = false;
                _lastTimestamp = null;
                return;
            }

            ScheduleNext(currentTopLevel);
        });
    }
}
