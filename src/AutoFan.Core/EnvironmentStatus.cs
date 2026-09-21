namespace AutoFan.Core;

public sealed record EnvironmentCheck(string Title, bool Passed, string Detail);

public sealed record EnvironmentStatus(
    IReadOnlyList<EnvironmentCheck> Checks,
    string? DiscoveryError)
{
    public const string CompetingSoftwareTitle = "Other fan software";

    public static EnvironmentStatus Create(
        bool isAdministrator,
        bool pawnIoPresent,
        string? discoveryError,
        IReadOnlyList<string>? competingSoftware = null)
    {
        IReadOnlyList<string> competing = competingSoftware ?? [];
        return new EnvironmentStatus(
            [
                new EnvironmentCheck(
                    "Administrator",
                    isAdministrator,
                    isAdministrator
                        ? "Running as Administrator."
                        : "AUTO Fan is not running as Administrator. Many motherboard sensors will be missing. Right-click the app and choose Run as administrator."),
                new EnvironmentCheck(
                    "PawnIO",
                    pawnIoPresent,
                    pawnIoPresent
                        ? "PawnIO is installed."
                        : "The PawnIO driver is not installed. CPU and motherboard Super I/O sensors need it. Install PawnIO, then reopen AUTO Fan. This app does not install the driver for you."),
                new EnvironmentCheck(
                    CompetingSoftwareTitle,
                    competing.Count == 0,
                    competing.Count == 0
                        ? "No competing fan-control software detected."
                        : "Close these apps before AUTO Fan changes fan speeds: "
                            + string.Join(", ", competing)),
            ],
            discoveryError);
    }
}
