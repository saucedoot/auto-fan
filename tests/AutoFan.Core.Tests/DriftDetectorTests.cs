using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class DriftDetectorTests
{
    [Fact]
    public void Ambient_shifted_flags_the_old_operating_point_as_stale()
    {
        PolicyConfirmation confirmation = Passed(ambient: 27);
        DriftAssessment assessment = DriftDetector.Assess(confirmation, 22, GpuInfluence());

        Assert.True(assessment.Stale);
        Assert.True(assessment.AmbientShifted);
        Assert.Equal(DriftAction.OfferRetest, assessment.Action);
        Assert.Contains(FakeHardwareBackend.FrontFanId, assessment.RetestGroupIds);
        Assert.Contains(FakeHardwareBackend.RearFanId, assessment.RetestGroupIds);
        Assert.Contains("room looks different", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Same_ambient_is_not_stale()
    {
        PolicyConfirmation confirmation = Passed(ambient: 22);
        DriftAssessment assessment = DriftDetector.Assess(confirmation, 22, GpuInfluence());

        Assert.False(assessment.Stale);
        Assert.Equal(DriftAction.None, assessment.Action);
        Assert.Empty(assessment.RetestGroupIds);
    }

    [Fact]
    public void Unknown_ambient_is_not_stale_from_the_room()
    {
        PolicyConfirmation confirmation = Passed(ambient: null);
        DriftAssessment assessment = DriftDetector.Assess(confirmation, referenceAmbientCelsius: null, GpuInfluence());

        Assert.False(assessment.Stale);
        Assert.Equal(DriftAction.None, assessment.Action);
    }

    [Fact]
    public void Miss_offers_a_targeted_retest_not_a_full_tour()
    {
        var confirmation = new PolicyConfirmation(80, 61, 76, 60, Missed: true, AddedAirflow: true, AmbientCelsius: 22);
        DriftAssessment assessment = DriftDetector.Assess(confirmation, 22, MixedInfluence());

        Assert.True(assessment.Stale);
        Assert.True(assessment.Missed);
        Assert.True(assessment.AddedAirflow);
        Assert.Equal(DriftAction.OfferRetest, assessment.Action);
        Assert.Contains(FakeHardwareBackend.RearFanId, assessment.RetestGroupIds);
        Assert.DoesNotContain(FakeHardwareBackend.FrontFanId, assessment.RetestGroupIds);
        Assert.DoesNotContain(FakeHardwareBackend.TopFanId, assessment.RetestGroupIds);
    }

    [Fact]
    public void Gpu_only_miss_retests_slight_gpu_groups_not_every_header()
    {
        var confirmation = new PolicyConfirmation(70, 78, 70, 72, Missed: true, AddedAirflow: true);
        DriftAssessment assessment = DriftDetector.Assess(confirmation, referenceAmbientCelsius: null, MixedInfluence());

        Assert.Equal(
            [FakeHardwareBackend.FrontFanId, FakeHardwareBackend.TopFanId],
            assessment.RetestGroupIds);
        Assert.DoesNotContain(FakeHardwareBackend.RearFanId, assessment.RetestGroupIds);
    }

    [Fact]
    public void Unknown_ambient_miss_can_still_offer_retest()
    {
        var confirmation = new PolicyConfirmation(80, 60, 76, 60, Missed: true, AddedAirflow: false);
        DriftAssessment assessment = DriftDetector.Assess(
            confirmation,
            referenceAmbientCelsius: null,
            MixedInfluence());

        Assert.False(assessment.AmbientShifted);
        Assert.Equal(DriftAction.OfferRetest, assessment.Action);
        Assert.Contains(FakeHardwareBackend.RearFanId, assessment.RetestGroupIds);
    }

    [Fact]
    public void Ignore_hides_the_same_offer_until_ambient_shifts_again()
    {
        PolicyConfirmation first = Passed(ambient: 27);
        DriftAssessment offered = DriftDetector.Assess(first, 22, GpuInfluence());
        DriftAssessment ignored = DriftDetector.Assess(Passed(ambient: 27), 22, GpuInfluence(), offered);

        Assert.True(ignored.Stale);
        Assert.Equal(DriftAction.None, ignored.Action);

        DriftAssessment again = DriftDetector.Assess(Passed(ambient: 31), 22, GpuInfluence(), offered);
        Assert.Equal(DriftAction.OfferRetest, again.Action);
    }

    [Fact]
    public void Assess_is_a_pure_function_with_no_hardware_or_network()
    {
        PolicyConfirmation confirmation = Passed(ambient: 27);
        DriftAssessment first = DriftDetector.Assess(confirmation, 22, GpuInfluence());
        DriftAssessment second = DriftDetector.Assess(confirmation, 22, GpuInfluence());

        Assert.Equal(first.Stale, second.Stale);
        Assert.Equal(first.Action, second.Action);
        Assert.Equal(first.Reason, second.Reason);
        Assert.Equal(first.RetestGroupIds, second.RetestGroupIds);
        Assert.DoesNotContain(
            typeof(DriftDetector).Assembly.GetReferencedAssemblies(),
            static name => name.Name is "System.Net.Http" or "System.Net.Requests");
    }

    [Fact]
    public void Confirmation_is_due_after_thirty_minutes_or_first_sustained_load()
    {
        DateTimeOffset now = new(2026, 9, 19, 20, 0, 0, TimeSpan.Zero);
        Assert.True(PolicyConfirmSchedule.IsDue(now, lastConfirmedAt: null, false, false));
        Assert.False(PolicyConfirmSchedule.IsDue(now, now.AddMinutes(-10), false, false));
        Assert.True(PolicyConfirmSchedule.IsDue(now, now.AddMinutes(-30), false, false));
        Assert.True(PolicyConfirmSchedule.IsDue(now, now.AddMinutes(-1), sustainedNonDesktop: true, alreadyConfirmedSustainedNonDesktop: false));
        Assert.False(PolicyConfirmSchedule.IsDue(now, now.AddMinutes(-1), sustainedNonDesktop: true, alreadyConfirmedSustainedNonDesktop: true));
    }

    private static PolicyConfirmation Passed(double? ambient) =>
        new(70, 65, 70, 65, Missed: false, AddedAirflow: false, AmbientCelsius: ambient);

    private static IReadOnlyList<InfluenceEntry> GpuInfluence() =>
    [
        Measured(FakeHardwareBackend.FrontFanId, "Front intake", InfluenceTarget.Gpu, 1.5, InfluenceEffect.Low),
        Measured(FakeHardwareBackend.RearFanId, "Rear exhaust", InfluenceTarget.Cpu, 4.0, InfluenceEffect.High),
        Measured(FakeHardwareBackend.RearFanId, "Rear exhaust", InfluenceTarget.Gpu, 0.2, InfluenceEffect.None),
    ];

    private static IReadOnlyList<InfluenceEntry> MixedInfluence() =>
    [
        Measured(FakeHardwareBackend.FrontFanId, "Front intake", InfluenceTarget.Gpu, 1.5, InfluenceEffect.Low),
        Measured(FakeHardwareBackend.TopFanId, "Top exhaust", InfluenceTarget.Gpu, 0.7, InfluenceEffect.Low),
        Measured(FakeHardwareBackend.RearFanId, "Rear exhaust", InfluenceTarget.Cpu, 4.0, InfluenceEffect.High),
        Measured(FakeHardwareBackend.RearFanId, "Rear exhaust", InfluenceTarget.Gpu, 0.2, InfluenceEffect.None),
    ];

    private static InfluenceEntry Measured(
        string id,
        string name,
        InfluenceTarget target,
        double delta,
        InfluenceEffect effect) =>
        new(id, name, target, delta, effect, MetricEvidence.Measured, 35, 60, 490, 840);
}
