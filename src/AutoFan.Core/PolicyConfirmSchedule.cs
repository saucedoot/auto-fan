namespace AutoFan.Core;

public static class PolicyConfirmSchedule
{
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(30);

    public static bool IsDue(
        DateTimeOffset now,
        DateTimeOffset? lastConfirmedAt,
        bool sustainedNonDesktop,
        bool alreadyConfirmedSustainedNonDesktop)
    {
        if (lastConfirmedAt is null)
        {
            return true;
        }

        if (now - lastConfirmedAt >= Period)
        {
            return true;
        }

        return sustainedNonDesktop && !alreadyConfirmedSustainedNonDesktop;
    }
}
