using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class CompetingSoftwareScannerTests
{
    [Fact]
    public void Match_returns_distinct_display_names_for_known_processes()
    {
        IReadOnlyList<string> found = CompetingSoftwareCatalog.Match(
            ["chrome", "FanControl", "iCUE", "ArmouryCrate.UserSessionHelper", "notepad"]);

        Assert.Equal(["Armoury Crate", "FanControl", "iCUE"], found);
    }

    [Fact]
    public void Match_is_case_insensitive_and_ignores_unknown_names()
    {
        IReadOnlyList<string> found = CompetingSoftwareCatalog.Match(["fancontrol", "explorer"]);

        Assert.Equal(["FanControl"], found);
    }

    [Fact]
    public void Scanner_uses_the_supplied_process_name_source()
    {
        var scanner = new CompetingSoftwareScanner(() => ["SignalRgb", "OpenRGB"]);

        Assert.Equal(["OpenRGB", "SignalRGB"], scanner.DetectRunning());
    }
}
