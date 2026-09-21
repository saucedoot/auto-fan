using AutoFan.App.ViewModels;
using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class OptimizeWalkViewModelTests
{
    [Fact]
    public void Start_opens_watch_without_running_fan_tests()
    {
        var walk = new OptimizeWalkViewModel();
        int primary = 0;
        walk.PrimaryRequested += (_, _) => primary++;

        Assert.Equal(OptimizeWalkStep.Consent, walk.Step);
        walk.RequestPrimary();

        Assert.Equal(OptimizeWalkStep.Watch, walk.Step);
        Assert.Equal(OptimizeWalkPhase.Ready, walk.Phase);
        Assert.Equal("Start watching", walk.PrimaryText);
        Assert.Equal(0, primary);
        Assert.False(walk.TestsStarted);
        Assert.False(walk.IsBusy);
    }

    [Fact]
    public void Continue_is_required_after_watching_before_fan_tests()
    {
        var walk = new OptimizeWalkViewModel();
        int primary = 0;
        walk.PrimaryRequested += (_, _) => primary++;
        walk.RequestPrimary();
        walk.BeginCurrent();
        walk.CompleteCurrent("Watching finished.");

        Assert.Equal("Continue", walk.PrimaryText);
        walk.RequestPrimary();

        Assert.Equal(OptimizeWalkStep.Fans, walk.Step);
        Assert.Equal(OptimizeWalkPhase.Ready, walk.Phase);
        Assert.Equal("Start fan tests", walk.PrimaryText);
        Assert.Equal(0, primary);

        walk.RequestPrimary();
        Assert.Equal(1, primary);
        Assert.Equal(OptimizeWalkStep.Fans, walk.Step);
    }

    [Fact]
    public void Aborted_watch_stays_on_watch_for_retry_not_continue()
    {
        var walk = new OptimizeWalkViewModel();
        int primary = 0;
        walk.PrimaryRequested += (_, _) => primary++;
        walk.RequestPrimary();
        walk.BeginCurrent();
        walk.Fail("CPU temperature reached the 90 °C abort limit.");

        Assert.Equal(OptimizeWalkStep.Watch, walk.Step);
        Assert.Equal(OptimizeWalkPhase.Ready, walk.Phase);
        Assert.Equal("Start watching", walk.PrimaryText);
        Assert.True(walk.CanPrimary);
        Assert.Contains("90", walk.StatusText, StringComparison.Ordinal);

        walk.RequestPrimary();
        Assert.Equal(1, primary);
        Assert.Equal(OptimizeWalkStep.Watch, walk.Step);
        Assert.NotEqual("Continue", walk.PrimaryText);
    }

    [Fact]
    public void Aborted_empty_fan_tests_stay_on_fans_for_retry()
    {
        var walk = new OptimizeWalkViewModel();
        walk.RequestPrimary();
        walk.BeginCurrent();
        walk.CompleteCurrent("Watching finished.");
        walk.RequestPrimary();
        walk.BeginCurrent();
        walk.Fail(OptimizeWalkOutcome.EmptyFanTestsDetail);

        Assert.Equal(OptimizeWalkStep.Fans, walk.Step);
        Assert.Equal(OptimizeWalkPhase.Ready, walk.Phase);
        Assert.Equal("Start fan tests", walk.PrimaryText);
        Assert.True(walk.CanPrimary);
        Assert.Equal(OptimizeWalkOutcome.EmptyFanTestsDetail, walk.StatusText);
    }

    [Fact]
    public void Cancel_is_available_until_hold_finishes()
    {
        var walk = new OptimizeWalkViewModel();
        int cancel = 0;
        walk.CancelRequested += (_, _) => cancel++;

        walk.RequestCancel();
        Assert.Equal(1, cancel);
        Assert.True(walk.CanCancel);

        walk.RequestPrimary();
        walk.BeginCurrent();
        walk.CompleteCurrent("Watching finished.");
        walk.RequestPrimary();
        walk.BeginCurrent();
        walk.CompleteCurrent("Fan tests finished.");
        walk.RequestPrimary();
        walk.BeginCurrent();
        walk.CompleteCurrent("Holding.");

        Assert.False(walk.CanCancel);
        walk.RequestCancel();
        Assert.Equal(1, cancel);
    }
}
