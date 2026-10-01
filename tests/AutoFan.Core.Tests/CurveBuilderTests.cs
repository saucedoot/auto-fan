using AutoFan.Core;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class CurveBuilderTests
{
    [Fact]
    public void Two_settled_points_stay_a_draft_with_a_safety_extension()
    {
        CoolingProfile profile = CurveBuilder.Build(
            [
                Sample("front", 40, 30),
                Sample("front", 60, 55),
            ],
            ThermalAbortLimits.Floor);

        Assert.Equal(ProfileState.Draft, profile.State);
        Assert.False(profile.IsValidated);
        Assert.Equal(2, profile.Points.Count(point => point.Evidence == MetricEvidence.Measured));
        CurvePoint safety = Assert.Single(profile.Points, point => point.Origin == CurvePointOrigin.SafetyExtension);
        Assert.Equal(100, safety.DutyPercent);
        Assert.Equal(ThermalAbortLimits.Floor.CpuCelsius - CurveBuilder.SafetyMarginCelsius, safety.TemperatureCelsius);
        Assert.NotEqual(MetricEvidence.Measured, safety.Evidence);
        Assert.DoesNotContain(profile.Points, point => point.DutyPercent == 0);
    }

    [Fact]
    public void One_settled_point_does_not_invent_a_curve()
    {
        CoolingProfile profile = CurveBuilder.Build(
            [Sample("front", 40, 30), Sample("front", 40.2, 80)],
            ThermalAbortLimits.Floor);

        Assert.Empty(profile.Points);
        Assert.Equal(CurveBuilder.NotEnoughDetail, profile.Detail);
        Assert.Equal(ProfileState.Draft, profile.State);
    }

    [Fact]
    public void A_falling_duty_is_lifted_and_needs_a_check()
    {
        CoolingProfile profile = CurveBuilder.Build(
            [
                Sample("front", 40, 50),
                Sample("front", 70, 30),
            ],
            ThermalAbortLimits.Floor);

        Assert.Equal(ProfileState.EditedCheckRequired, profile.State);
        CurvePoint corrected = profile.Points.Single(point => point.Origin == CurvePointOrigin.MonotoneCorrection);
        Assert.Equal(50, corrected.DutyPercent);
        Assert.NotEqual(MetricEvidence.Measured, corrected.Evidence);
    }

    [Fact]
    public void The_coolest_point_does_not_go_below_the_minimum_stable_duty()
    {
        CoolingProfile profile = CurveBuilder.Build(
            [
                Sample("front", 35, 20),
                Sample("front", 55, 40),
            ],
            ThermalAbortLimits.Floor,
            new Dictionary<string, int> { ["front"] = 30 });

        Assert.Equal(30, profile.Points[0].DutyPercent);
        Assert.Equal(CurvePointOrigin.DutyFloor, profile.Points[0].Origin);
        Assert.Equal(ProfileState.EditedCheckRequired, profile.State);
    }

    [Fact]
    public void A_user_ceiling_can_only_bring_the_safety_point_sooner()
    {
        CoolingProfile sooner = CurveBuilder.Build(
            [
                Sample("gpu", 40, 40, CurveSensor.Gpu),
                Sample("gpu", 60, 70, CurveSensor.Gpu),
            ],
            ThermalAbortLimits.Floor,
            userGpuCeiling: 70);

        CurvePoint safety = sooner.Points.Single(point => point.Origin == CurvePointOrigin.SafetyExtension);
        Assert.Equal(70, safety.TemperatureCelsius);

        CoolingProfile notLater = CurveBuilder.Build(
            [
                Sample("gpu", 40, 40, CurveSensor.Gpu),
                Sample("gpu", 60, 70, CurveSensor.Gpu),
            ],
            ThermalAbortLimits.Floor,
            userGpuCeiling: 200);

        CurvePoint capped = notLater.Points.Single(point => point.Origin == CurvePointOrigin.SafetyExtension);
        Assert.Equal(ThermalAbortLimits.Floor.GpuCelsius - CurveBuilder.SafetyMarginCelsius, capped.TemperatureCelsius);
    }

    [Fact]
    public void A_draft_round_trip_keeps_points_and_does_not_migrate_twice()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autofan-curve-{Guid.NewGuid():N}.db");
        string connection = $"Data Source={path};Pooling=False";
        CoolingProfile profile = CurveBuilder.Build(
            [
                Sample("front", 40, 30),
                Sample("front", 60, 55),
            ],
            ThermalAbortLimits.Floor);
        try
        {
            using (var store = new SqliteCurveStore(connection))
            {
                Assert.Equal(SqliteCurveStore.CurrentVersion, store.SchemaVersion);
                store.Save(profile);
            }

            using var reopened = new SqliteCurveStore(connection);
            Assert.Equal(SqliteCurveStore.CurrentVersion, reopened.SchemaVersion);
            CoolingProfile? loaded = reopened.GetLatest();
            Assert.NotNull(loaded);
            Assert.Equal(profile.State, loaded.State);
            Assert.Equal(profile.Points.Count, loaded.Points.Count);
            Assert.Equal(profile.Points[0].DutyPercent, loaded.Points[0].DutyPercent);
            Assert.Equal(profile.Points[^1].Origin, loaded.Points[^1].Origin);
            Assert.Throws<InvalidOperationException>(() => reopened.Save(profile with { State = ProfileState.Validated }));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static CurveSample Sample(
        string group,
        double temperature,
        int duty,
        CurveSensor sensor = CurveSensor.Cpu) =>
        new(group, sensor, temperature, duty, Settled: true);
}
