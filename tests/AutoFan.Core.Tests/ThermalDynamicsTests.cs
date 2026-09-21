using System.Text.Json;
using System.Text.Json.Serialization;
using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class ThermalDynamicsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Summarize_computes_rise_decay_and_settle_from_a_fixture_series()
    {
        IReadOnlyList<BaselineSample> samples = LoadFixture();

        IReadOnlyList<BaselineMetric> metrics = ThermalDynamics.Summarize(samples);

        BaselineMetric ambient = Metric(metrics, BaselineMetricNames.AmbientCelsius);
        Assert.Equal(MetricEvidence.Measured, ambient.Evidence);
        Assert.Equal(24, ambient.Value);

        Assert.Equal(
            MetricEvidence.Unknown,
            Metric(metrics, BaselineMetricNames.CpuEverydayRiseCelsius).Evidence);
        Assert.Equal(
            MetricEvidence.Unknown,
            Metric(metrics, BaselineMetricNames.GpuEverydayRiseCelsius).Evidence);

        BaselineMetric cpuRise = Metric(metrics, BaselineMetricNames.CpuRiseCelsius);
        Assert.Equal(MetricEvidence.Measured, cpuRise.Evidence);
        Assert.InRange(cpuRise.Value ?? 0, 26, 28);

        BaselineMetric gpuRise = Metric(metrics, BaselineMetricNames.GpuRiseCelsius);
        Assert.Equal(MetricEvidence.Measured, gpuRise.Evidence);
        Assert.InRange(gpuRise.Value ?? 0, 29, 31);

        BaselineMetric cpuDecay = Metric(metrics, BaselineMetricNames.CpuDecayCelsius);
        Assert.Equal(MetricEvidence.Measured, cpuDecay.Evidence);
        Assert.True(cpuDecay.Value > 20);

        BaselineMetric gpuDecay = Metric(metrics, BaselineMetricNames.GpuDecayCelsius);
        Assert.Equal(MetricEvidence.Measured, gpuDecay.Evidence);
        Assert.True(gpuDecay.Value > 20);

        BaselineMetric cpuSettle = Metric(metrics, BaselineMetricNames.CpuSettleSeconds);
        Assert.Equal(MetricEvidence.Measured, cpuSettle.Evidence);
        Assert.True(cpuSettle.Value >= 0);
    }

    [Fact]
    public void Summarize_records_missing_ambient_as_unknown()
    {
        DateTimeOffset start = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        BaselineSample[] samples =
        [
            Sample(start, BaselinePhase.Idle, 45, 40, ambient: null),
            Sample(start.AddSeconds(1), BaselinePhase.High, 70, 65, ambient: null),
            Sample(start.AddSeconds(2), BaselinePhase.Cooldown, 50, 48, ambient: null),
        ];

        IReadOnlyList<BaselineMetric> metrics = ThermalDynamics.Summarize(samples);

        BaselineMetric ambient = Metric(metrics, BaselineMetricNames.AmbientCelsius);
        Assert.Equal(MetricEvidence.Unknown, ambient.Evidence);
        Assert.Null(ambient.Value);
        Assert.Null(ThermalDynamics.FirstAmbient(samples));
    }

    [Fact]
    public void Summarize_marks_rise_unknown_when_a_phase_is_missing()
    {
        DateTimeOffset start = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        BaselineSample[] samples =
        [
            Sample(start, BaselinePhase.Idle, 45, 40, ambient: 22),
        ];

        IReadOnlyList<BaselineMetric> metrics = ThermalDynamics.Summarize(samples);

        Assert.Equal(MetricEvidence.Unknown, Metric(metrics, BaselineMetricNames.CpuRiseCelsius).Evidence);
        Assert.Null(Metric(metrics, BaselineMetricNames.CpuRiseCelsius).Value);
        Assert.Equal(MetricEvidence.Unknown, Metric(metrics, BaselineMetricNames.CpuEverydayRiseCelsius).Evidence);
        Assert.Equal(MetricEvidence.Unknown, Metric(metrics, BaselineMetricNames.CpuSettleSeconds).Evidence);
        Assert.Equal(MetricEvidence.Measured, Metric(metrics, BaselineMetricNames.AmbientCelsius).Evidence);
    }

    [Fact]
    public void Summarize_computes_everyday_rise_without_changing_high_rise()
    {
        DateTimeOffset start = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        BaselineSample[] samples =
        [
            Sample(start, BaselinePhase.Idle, 45, 40, ambient: 22),
            Sample(start.AddSeconds(1), BaselinePhase.Everyday, 58, 50, ambient: 22),
            Sample(start.AddSeconds(2), BaselinePhase.High, 80, 72, ambient: 22),
            Sample(start.AddSeconds(3), BaselinePhase.Cooldown, 50, 48, ambient: 22),
        ];

        IReadOnlyList<BaselineMetric> metrics = ThermalDynamics.Summarize(samples);

        BaselineMetric everydayCpu = Metric(metrics, BaselineMetricNames.CpuEverydayRiseCelsius);
        Assert.Equal(MetricEvidence.Measured, everydayCpu.Evidence);
        Assert.Equal(13, everydayCpu.Value);
        BaselineMetric everydayGpu = Metric(metrics, BaselineMetricNames.GpuEverydayRiseCelsius);
        Assert.Equal(MetricEvidence.Measured, everydayGpu.Evidence);
        Assert.Equal(10, everydayGpu.Value);
        Assert.Equal(35, Metric(metrics, BaselineMetricNames.CpuRiseCelsius).Value);
        Assert.Equal(32, Metric(metrics, BaselineMetricNames.GpuRiseCelsius).Value);
    }

    [Fact]
    public void Summarize_uses_low_heat_for_rise_when_high_is_absent()
    {
        DateTimeOffset start = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        BaselineSample[] samples =
        [
            Sample(start, BaselinePhase.Idle, 45, 40, ambient: 22),
            Sample(start.AddSeconds(1), BaselinePhase.Everyday, 58, 50, ambient: 22),
            Sample(start.AddSeconds(2), BaselinePhase.Low, 72, 55, ambient: 22),
            Sample(start.AddSeconds(3), BaselinePhase.Cooldown, 50, 48, ambient: 22),
        ];

        IReadOnlyList<BaselineMetric> metrics = ThermalDynamics.Summarize(samples);

        Assert.Equal(27, Metric(metrics, BaselineMetricNames.CpuRiseCelsius).Value);
        Assert.Equal(15, Metric(metrics, BaselineMetricNames.GpuRiseCelsius).Value);
        Assert.Equal(13, Metric(metrics, BaselineMetricNames.CpuEverydayRiseCelsius).Value);
    }

    private static IReadOnlyList<BaselineSample> LoadFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "baseline-time-series.json");
        string json = File.ReadAllText(path);
        FixtureFile? file = JsonSerializer.Deserialize<FixtureFile>(json, JsonOptions);
        Assert.NotNull(file);
        DateTimeOffset start = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        return file.Samples.Select(row => Sample(
            start.AddSeconds(row.OffsetSeconds),
            row.Phase,
            row.Cpu,
            row.Gpu,
            row.Ambient)).ToArray();
    }

    private static BaselineSample Sample(
        DateTimeOffset at,
        BaselinePhase phase,
        double cpu,
        double gpu,
        double? ambient)
    {
        var sensors = new List<SensorReading>
        {
            new("cpu-temp", "CPU", SensorKind.CpuTemperature, cpu, "°C"),
            new("gpu-temp", "GPU", SensorKind.GpuTemperature, gpu, "°C"),
        };
        if (ambient is double value)
        {
            sensors.Add(new SensorReading("ambient-temp", "Ambient", SensorKind.AmbientTemperature, value, "°C"));
        }

        return new BaselineSample(
            at,
            phase,
            new HardwareSnapshot(at, sensors, [], IsDemoHardware: true));
    }

    private static BaselineMetric Metric(IReadOnlyList<BaselineMetric> metrics, string name) =>
        metrics.Single(metric => metric.Name == name);

    private sealed record FixtureFile(IReadOnlyList<FixtureRow> Samples);

    private sealed record FixtureRow(
        int OffsetSeconds,
        BaselinePhase Phase,
        double Cpu,
        double Gpu,
        double? Ambient);
}
