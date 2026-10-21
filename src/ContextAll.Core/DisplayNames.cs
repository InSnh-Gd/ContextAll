using System.Text;

namespace ContextAll.Core;

public static class DisplayNames
{
    public static bool IsLegal(string name)
    {
        var count = 0;
        foreach (var rune in name.EnumerateRunes())
        {
            if (rune.Value < 0x20)
            {
                return false;
            }

            count++;
            if (count > 64)
            {
                return false;
            }
        }

        return count >= 1;
    }

    public static string Initial(string? hostName)
    {
        if (string.IsNullOrEmpty(hostName))
        {
            return "ContextAll";
        }

        var kept = new StringBuilder();
        var count = 0;
        foreach (var rune in hostName.EnumerateRunes())
        {
            if (rune.Value < 0x20)
            {
                continue;
            }

            kept.Append(rune.ToString());
            count++;
            if (count == 64)
            {
                break;
            }
        }

        return kept.Length == 0 ? "ContextAll" : kept.ToString();
    }
}
