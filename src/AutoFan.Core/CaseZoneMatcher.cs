namespace AutoFan.Core;

public static class CaseZoneMatcher
{
    public static CaseZone FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CaseZone.Unknown;
        }

        string text = name.Replace('_', ' ');
        if (Contains(text, "cpu fan"))
        {
            return CaseZone.CpuHeader;
        }

        if (Contains(text, "front"))
        {
            return CaseZone.Front;
        }

        if (Contains(text, "bottom"))
        {
            return CaseZone.Bottom;
        }

        if (Contains(text, "rear"))
        {
            return CaseZone.Rear;
        }

        if (Contains(text, "top"))
        {
            return CaseZone.Top;
        }

        return CaseZone.Unknown;
    }

    private static bool Contains(string text, string token) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
