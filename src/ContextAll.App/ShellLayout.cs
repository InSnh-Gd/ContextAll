namespace ContextAll.App;

public static class ShellLayout
{
    public const int MinimumWidth = 480;

    public const int MinimumHeight = 640;

    public const int TwoColumnWidth = 840;

    public static bool TwoColumns(double width) => width >= TwoColumnWidth;

    public static (bool List, bool Detail) Columns(double width, bool hasSelection) =>
        TwoColumns(width) ? (true, true) : hasSelection ? (false, true) : (true, false);
}
