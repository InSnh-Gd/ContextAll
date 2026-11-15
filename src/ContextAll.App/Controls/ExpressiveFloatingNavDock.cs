namespace ContextAll.App.Controls;

/// <summary>
/// Compatibility alias inheriting <see cref="HorizontalFloatingToolbar"/> for existing navigation dock call sites.
/// </summary>
public class ExpressiveFloatingNavDock : HorizontalFloatingToolbar
{
    protected override Type StyleKeyOverride => typeof(HorizontalFloatingToolbar);
}
