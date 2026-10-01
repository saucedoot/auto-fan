using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class HomeCurveBoardTests
{
    [Fact]
    public void A_draft_shows_measured_modeled_and_safety_and_stays_off()
    {
        CoolingProfile profile = CurveBuilder.Build(
            [
                new CurveSample("front", CurveSensor.Cpu, 40, 30, true),
                new CurveSample("front", CurveSensor.Cpu, 70, 20, true),
                new CurveSample("front", CurveSensor.Gpu, 45, 40, true),
                new CurveSample("front", CurveSensor.Gpu, 75, 80, true),
            ],
            ThermalAbortLimits.Floor);

        HomeCurvePage page = HomeCurveBoard.Build(profile, ["front", "rear"]);

        Assert.Equal("Edited — check required", page.StateLabel);
        Assert.Contains("BIOS", page.ControlLabel, StringComparison.Ordinal);
        HomeCurveGroup front = Assert.Single(page.Groups, group => group.GroupId == "front");
        Assert.Equal("Uses the higher of the CPU and GPU requests.", front.DriverLabel);
        Assert.Contains(front.Points, point => point.Kind == "Measured");
        Assert.Contains(front.Points, point => point.Kind == "Modeled");
        Assert.Contains(front.Points, point => point.Kind == "Safety" && !point.CanEdit);
        HomeCurveGroup rear = Assert.Single(page.Groups, group => group.GroupId == "rear");
        Assert.True(rear.StaysOnBios);
        Assert.Contains(rear.Points, point => point.Kind == "Unknown");
    }

    [Fact]
    public void An_edit_needs_a_check_and_cannot_remove_the_safety_point()
    {
        CoolingProfile profile = CurveBuilder.Build(
            [
                new CurveSample("front", CurveSensor.Cpu, 40, 30, true),
                new CurveSample("front", CurveSensor.Cpu, 70, 60, true),
            ],
            ThermalAbortLimits.Floor);
        CurvePoint safety = profile.Points.Single(point => point.Origin == CurvePointOrigin.SafetyExtension);

        Assert.False(CurveEdit.TryApply(
            profile,
            "front",
            CurveSensor.Cpu,
            safety.TemperatureCelsius,
            40,
            out _,
            out string? blocked));
        Assert.Equal("The safety point stays.", blocked);

        Assert.True(CurveEdit.TryApply(
            profile,
            "front",
            CurveSensor.Cpu,
            40,
            70,
            out CoolingProfile edited,
            out string? error));
        Assert.Null(error);
        Assert.Equal(ProfileState.EditedCheckRequired, edited.State);
        Assert.NotEqual(ProfileState.Validated, edited.State);
        Assert.False(edited.IsValidated);
        Assert.Contains(edited.Points, point => point.Origin == CurvePointOrigin.SafetyExtension && point.DutyPercent == 100);
        Assert.Equal(70, edited.Points.Single(point => point.TemperatureCelsius == 40).DutyPercent);
    }
}
