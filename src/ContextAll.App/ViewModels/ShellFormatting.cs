namespace ContextAll.App.ViewModels;

public static class ShellFormatting
{
    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KiB",
        _ => $"{bytes / (1024.0 * 1024.0):0.00} MiB",
    };

    public static string Outcome(string outcome) => outcome switch
    {
        "done" or "completed" => "已完成",
        "rejected" => "已拒绝",
        "cancelled" or "canceled" => "已取消",
        _ => outcome,
    };

    public static string ShortId(string id) => id.Length <= 8 ? id : id[..8];
}
