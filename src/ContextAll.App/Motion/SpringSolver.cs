namespace ContextAll.App.Motion;

public enum MotionRole
{
    FastSpatial = 0,
    DefaultSpatial = 1,
    SlowSpatial = 2,
    FastEffects = 3,
    DefaultEffects = 4,
    SlowEffects = 5,
}

public readonly record struct SpringSpec(double DampingRatio, double Stiffness);

public static class MotionScheme
{
    /// <summary>FerroUI / AndroidX compose.material3:1.5.0-beta02 MotionScheme.Expressive spring tokens.</summary>
    public static SpringSpec Expressive(MotionRole role) => role switch
    {
        MotionRole.FastSpatial => new(DampingRatio: 0.60, Stiffness: 800.0),
        MotionRole.DefaultSpatial => new(DampingRatio: 0.80, Stiffness: 380.0),
        MotionRole.SlowSpatial => new(DampingRatio: 0.80, Stiffness: 200.0),
        MotionRole.FastEffects => new(DampingRatio: 1.00, Stiffness: 3800.0),
        MotionRole.DefaultEffects => new(DampingRatio: 1.00, Stiffness: 1600.0),
        MotionRole.SlowEffects => new(DampingRatio: 1.00, Stiffness: 800.0),
        _ => new(DampingRatio: 0.80, Stiffness: 380.0),
    };
}

/// <summary>Closed-form damped second-order spring solver with velocity-preserving retargeting.</summary>
public sealed class SpringSolver
{
    private const double PositionEpsilon = 0.0005;
    private const double VelocityEpsilon = 0.005;

    private SpringSpec _spec;
    private bool _reducedMotion;

    public SpringSolver(SpringSpec spec, double initialValue = 0.0)
    {
        _spec = spec;
        Value = initialValue;
        Target = initialValue;
        Velocity = 0.0;
    }

    public SpringSpec Spec
    {
        get => _spec;
        set => _spec = value;
    }

    public double Value { get; private set; }
    public double Target { get; private set; }
    public double Velocity { get; private set; }

    public bool ReducedMotion
    {
        get => _reducedMotion;
        set
        {
            _reducedMotion = value;
            if (_reducedMotion)
            {
                SnapToTarget();
            }
        }
    }

    public bool IsSettled =>
        Math.Abs(Target - Value) <= PositionEpsilon &&
        Math.Abs(Velocity) <= VelocityEpsilon;

    public void SetTarget(double target)
    {
        Target = target;
        if (_reducedMotion)
        {
            SnapToTarget();
        }
    }

    public void SnapTo(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0.0;
    }

    public void AddVelocity(double deltaVelocity)
    {
        if (!_reducedMotion)
        {
            Velocity += deltaVelocity;
        }
    }

    public double Step(double deltaSeconds)
    {
        if (_reducedMotion)
        {
            SnapToTarget();
            return Value;
        }

        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0.0)
        {
            return Value;
        }

        // Clamp extremely long stalls (e.g. debugger/suspend) to 50ms while preserving analytical stability.
        var dt = Math.Min(deltaSeconds, 0.05);
        var dampingRatio = Math.Max(0.05, _spec.DampingRatio);
        var stiffness = Math.Max(1.0, _spec.Stiffness);
        var omega = Math.Sqrt(stiffness);

        var displacement = Value - Target;
        var initialVelocity = Velocity;

        double nextDisplacement;
        double nextVelocity;

        if (dampingRatio < 1.0 - 1e-6)
        {
            // Underdamped harmonic oscillator
            var omegaD = omega * Math.Sqrt(1.0 - dampingRatio * dampingRatio);
            var envelope = Math.Exp(-dampingRatio * omega * dt);
            var cos = Math.Cos(omegaD * dt);
            var sin = Math.Sin(omegaD * dt);
            var coeffA = displacement;
            var coeffB = (initialVelocity + dampingRatio * omega * displacement) / omegaD;

            nextDisplacement = envelope * (coeffA * cos + coeffB * sin);
            nextVelocity = envelope * (
                (coeffB * omegaD - coeffA * dampingRatio * omega) * cos -
                (coeffA * omegaD + coeffB * dampingRatio * omega) * sin);
        }
        else if (dampingRatio > 1.0 + 1e-6)
        {
            // Overdamped oscillator
            var gamma = omega * Math.Sqrt(dampingRatio * dampingRatio - 1.0);
            var r1 = -omega * dampingRatio + gamma;
            var r2 = -omega * dampingRatio - gamma;
            var c2 = (initialVelocity - r1 * displacement) / (r2 - r1);
            var c1 = displacement - c2;
            var e1 = Math.Exp(r1 * dt);
            var e2 = Math.Exp(r2 * dt);

            nextDisplacement = c1 * e1 + c2 * e2;
            nextVelocity = c1 * r1 * e1 + c2 * r2 * e2;
        }
        else
        {
            // Critically damped oscillator (zeta == 1.0)
            var envelope = Math.Exp(-omega * dt);
            var c1 = displacement;
            var c2 = initialVelocity + omega * displacement;

            nextDisplacement = envelope * (c1 + c2 * dt);
            nextVelocity = envelope * (c2 - omega * (c1 + c2 * dt));
        }

        Value = Target + nextDisplacement;
        Velocity = nextVelocity;

        if (IsSettled)
        {
            SnapToTarget();
        }

        return Value;
    }

    private void SnapToTarget()
    {
        Value = Target;
        Velocity = 0.0;
    }
}
