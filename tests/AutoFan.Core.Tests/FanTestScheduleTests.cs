using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class FanTestScheduleTests
{
    [Fact]
    public void AbsoluteDuties_bios_70_still_includes_below_bios_loud_first()
    {
        IReadOnlyList<int> duties = FanTestSchedule.AbsoluteDuties(FanTestSchedule.ScreenDuties, biosDuty: 70);

        Assert.Equal([100, 90, 75, 60, 45, 30, 15], duties);
        Assert.Contains(15, duties);
        Assert.Contains(30, duties);
        Assert.Contains(45, duties);
        Assert.Contains(60, duties);
        Assert.DoesNotContain(duties, duty => duty <= 0);
    }

    [Fact]
    public void AbsoluteDuties_bios_100_skips_only_the_already_commanded_max()
    {
        IReadOnlyList<int> duties = FanTestSchedule.AbsoluteDuties(FanTestSchedule.ScreenDuties, biosDuty: 100);

        Assert.Equal([90, 75, 60, 45, 30, 15], duties);
        Assert.DoesNotContain(100, duties);
        Assert.False(FanTestSchedule.IsAlreadyAtMax(100, duties));
    }

    [Fact]
    public void AbsoluteDuties_skips_already_commanded_duties_and_orders_refine_loud_first()
    {
        IReadOnlyList<int> duties = FanTestSchedule.AbsoluteDuties(
            FanTestSchedule.RefineDuties,
            biosDuty: 70,
            alreadyCommanded: new HashSet<int> { 70, 40 });

        Assert.Equal([85, 50, 20], duties);
    }

    [Fact]
    public void IsAlreadyAtMax_only_when_bios_is_100_and_nothing_remains()
    {
        Assert.True(FanTestSchedule.IsAlreadyAtMax(100, []));
        Assert.False(FanTestSchedule.IsAlreadyAtMax(70, []));
        Assert.False(FanTestSchedule.IsAlreadyAtMax(100, [15, 30]));
    }

    [Fact]
    public void AbsoluteDuties_everyday_coarse_grid_is_below_and_above_bios_loud_first()
    {
        IReadOnlyList<int> duties = FanTestSchedule.AbsoluteDuties(
            FanTestSchedule.EverydayScreenDuties,
            biosDuty: 70);

        Assert.Equal([100, 70, 40, 20], duties);
        Assert.Equal([20, 40, 70, 100], FanTestSchedule.EverydayScreenDuties);
        Assert.Equal([55, 85], FanTestSchedule.EverydayRefineDuties);
    }
}
