namespace ContextAll.App.Controls;

public static class GroupPositions
{
    public static GroupPosition Of(int index, int count) => count switch
    {
        <= 1 => GroupPosition.Single,
        _ when index == 0 => GroupPosition.First,
        _ when index == count - 1 => GroupPosition.Last,
        _ => GroupPosition.Middle,
    };
}
