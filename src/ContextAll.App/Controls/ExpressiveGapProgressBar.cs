namespace ContextAll.App.Controls;

/// <summary>
/// Compatibility alias inheriting <see cref="LinearWavyProgressIndicator"/> for existing call sites and tests.
/// </summary>
public class ExpressiveGapProgressBar : LinearWavyProgressIndicator
{
    public const double GapWidth = DefaultGapWidth;

    protected override Type StyleKeyOverride => typeof(LinearWavyProgressIndicator);
}
