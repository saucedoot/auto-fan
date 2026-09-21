using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class InteractionBuilderTests
{
    [Fact]
    public void Build_detects_extra_combined_cooling_from_app_md_example()
    {
        IReadOnlyList<InteractionSample> samples = Pair(
            firstId: "front-intake",
            firstName: "Front intake",
            secondId: "top-exhaust",
            secondName: "Top exhaust",
            firstCooling: 1.5,
            secondCooling: 0.7,
            combinedCooling: 3.8);

        IReadOnlyList<InteractionEntry> map = InteractionBuilder.Build(samples);

        InteractionEntry gpu = map.Single(entry => entry.Target == InfluenceTarget.Gpu);
        Assert.Equal(MetricEvidence.Measured, gpu.Evidence);
        Assert.NotEqual(MetricEvidence.Inferred, gpu.Evidence);
        Assert.Equal(1.5, gpu.FirstDeltaCelsius ?? 0, 1);
        Assert.Equal(0.7, gpu.SecondDeltaCelsius ?? 0, 1);
        Assert.Equal(3.8, gpu.CombinedDeltaCelsius ?? 0, 1);
        Assert.Equal(1.6, gpu.ResidualCelsius ?? 0, 1);
        Assert.Contains("GPU", gpu.InferredNote, StringComparison.Ordinal);
        Assert.Contains("share", gpu.InferredNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("guess", gpu.InferredNote, StringComparison.OrdinalIgnoreCase);
        Assert.All(map, entry => Assert.NotEqual(MetricEvidence.Inferred, entry.Evidence));
    }

    [Fact]
    public void Build_omits_inferred_note_when_residual_is_inside_none_band()
    {
        IReadOnlyList<InteractionSample> samples = Pair(
            "front-intake",
            "Front intake",
            "top-exhaust",
            "Top exhaust",
            firstCooling: 1.0,
            secondCooling: 1.0,
            combinedCooling: 2.1);

        InteractionEntry gpu = InteractionBuilder.Build(samples).Single(entry => entry.Target == InfluenceTarget.Gpu);

        Assert.Equal(MetricEvidence.Measured, gpu.Evidence);
        Assert.InRange(gpu.ResidualCelsius ?? 0, -0.2, 0.2);
        Assert.Null(gpu.InferredNote);
    }

    [Fact]
    public void Build_records_missing_case_as_unknown_not_omitted()
    {
        IReadOnlyList<InteractionSample> samples = Pair(
            "front-intake",
            "Front intake",
            "top-exhaust",
            "Top exhaust",
            firstCooling: 1.5,
            secondCooling: 0.7,
            combinedCooling: 3.8,
            includeCase: false);

        IReadOnlyList<InteractionEntry> map = InteractionBuilder.Build(samples);
        InteractionEntry caseEntry = map.Single(entry => entry.Target == InfluenceTarget.Case);

        Assert.Equal(4, map.Count);
        Assert.Equal(MetricEvidence.Unknown, caseEntry.Evidence);
        Assert.Null(caseEntry.ResidualCelsius);
        Assert.Null(caseEntry.InferredNote);
    }

    [Fact]
    public void InferNote_never_writes_into_measured_delta_fields()
    {
        var entry = new InteractionEntry(
            "a",
            "A",
            "b",
            "B",
            InfluenceTarget.Gpu,
            1.5,
            0.7,
            3.8,
            1.6,
            MetricEvidence.Measured,
            InteractionBuilder.InferNote(1.6, InfluenceTarget.Gpu));

        Assert.Equal(1.5, entry.FirstDeltaCelsius);
        Assert.Equal(0.7, entry.SecondDeltaCelsius);
        Assert.Equal(3.8, entry.CombinedDeltaCelsius);
        Assert.Equal(1.6, entry.ResidualCelsius);
        Assert.Equal(MetricEvidence.Measured, entry.Evidence);
        Assert.False(string.IsNullOrWhiteSpace(entry.InferredNote));
    }

    private static IReadOnlyList<InteractionSample> Pair(
        string firstId,
        string firstName,
        string secondId,
        string secondName,
        double firstCooling,
        double secondCooling,
        double combinedCooling,
        bool includeCase = true)
    {
        const double reference = 50;
        DateTimeOffset start = new(2026, 9, 19, 20, 0, 0, TimeSpan.Zero);
        return
        [
            .. Block(start, firstId, firstName, secondId, secondName, InteractionStep.ReferenceFirst, reference, includeCase),
            .. Block(start.AddSeconds(45), firstId, firstName, secondId, secondName, InteractionStep.First, reference - firstCooling, includeCase),
            .. Block(start.AddMinutes(2), firstId, firstName, secondId, secondName, InteractionStep.ReferenceSecond, reference, includeCase),
            .. Block(start.AddMinutes(2).AddSeconds(45), firstId, firstName, secondId, secondName, InteractionStep.Second, reference - secondCooling, includeCase),
            .. Block(start.AddMinutes(4), firstId, firstName, secondId, secondName, InteractionStep.ReferenceCombined, reference, includeCase),
            .. Block(start.AddMinutes(4).AddSeconds(45), firstId, firstName, secondId, secondName, InteractionStep.Combined, reference - combinedCooling, includeCase),
        ];
    }

    private static IReadOnlyList<InteractionSample> Block(
        DateTimeOffset start,
        string firstId,
        string firstName,
        string secondId,
        string secondName,
        InteractionStep step,
        double gpu,
        bool includeCase)
    {
        int count = step.ToString().StartsWith("Reference", StringComparison.Ordinal)
            ? 1
            : ThermalDynamics.SettleWindowSamples;
        var samples = new List<InteractionSample>(count);
        for (int index = 0; index < count; index++)
        {
            samples.Add(new InteractionSample(
                start.AddSeconds(index),
                firstId,
                firstName,
                secondId,
                secondName,
                step,
                Snapshot(gpu, includeCase)));
        }

        return samples;
    }

    private static HardwareSnapshot Snapshot(double gpu, bool includeCase)
    {
        var sensors = new List<SensorReading>
        {
            new("cpu-temp", "CPU", SensorKind.CpuTemperature, 60, "°C"),
            new("gpu-vr", "GPU VR SoC", SensorKind.GpuTemperature, 80, "°C"),
            new("gpu-core", "GPU Core", SensorKind.GpuTemperature, gpu, "°C"),
            new("vrm-temp", "VRM", SensorKind.VrmTemperature, 55, "°C"),
        };
        if (includeCase)
        {
            sensors.Add(new SensorReading("case-temp", "Case", SensorKind.CaseTemperature, 40, "°C"));
        }

        return new HardwareSnapshot(DateTimeOffset.UtcNow, sensors, [], IsDemoHardware: true);
    }
}
