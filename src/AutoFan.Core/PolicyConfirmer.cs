namespace AutoFan.Core;

public static class PolicyConfirmer
{
    public const double MissCelsius = 2.0;

    public const int ExtraDutyPercent = 5;

    public static (double? Cpu, double? Gpu) ExpectedSettled(
        double? startCpu,
        double? startGpu,
        ThermalModel model,
        CoolingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(policy);

        IReadOnlyList<string> groupIds = policy.Groups.Select(static group => group.FanGroupId).ToArray();
        return (
            ApplyDelta(startCpu, model.Predict(groupIds, InfluenceTarget.Cpu)),
            FloorGpu(
                startGpu,
                ApplyDelta(startGpu, model.Predict(groupIds, InfluenceTarget.Gpu)),
                ConfirmationDiagnosis.MeanPhaseTemperature(
                    model.Baseline,
                    BaselinePhase.Idle,
                    SensorKind.GpuTemperature)));
    }

    public static bool IsMiss(double? measured, double? expected) =>
        measured is double live && expected is double target && live > target + MissCelsius;

    public static bool Missed(double? measuredCpu, double? expectedCpu, double? measuredGpu, double? expectedGpu) =>
        IsMiss(measuredCpu, expectedCpu) || IsMiss(measuredGpu, expectedGpu);

    public static PolicyConfirmation Complete(
        double? measuredCpu,
        double? measuredGpu,
        double? expectedCpu,
        double? expectedGpu,
        bool addedAirflow,
        DateTimeOffset observedAt,
        double? ambientCelsius)
    {
        return new PolicyConfirmation(
            measuredCpu,
            measuredGpu,
            expectedCpu,
            expectedGpu,
            Missed(measuredCpu, expectedCpu, measuredGpu, expectedGpu),
            addedAirflow,
            Guid.NewGuid(),
            observedAt,
            ambientCelsius);
    }

    private static double? ApplyDelta(double? start, ThermalPrediction prediction)
    {
        if (start is not double value || prediction.DeltaCelsius is not double delta)
        {
            return null;
        }

        return value - delta;
    }

    private static double? FloorGpu(double? start, double? expected, double? idle)
    {
        if (start is not double startGpu || expected is not double guess)
        {
            return expected;
        }

        double floor = startGpu;
        if (idle is double idleGpu && startGpu >= idleGpu)
        {
            floor = idleGpu;
        }

        return Math.Max(guess, floor);
    }
}
