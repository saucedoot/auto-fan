using AutoFan.App.ViewModels;
using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void Binds_fixture_discovery_without_a_live_machine()
    {
        MappedHardware mapped = SensorTreeMapperTests.MapFixture();
        var session = new DummySessionResult(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DummySessionStatus.Observed,
            mapped.Snapshot,
            Detail: null);
        EnvironmentStatus environment = EnvironmentStatus.Create(
            isAdministrator: false,
            pawnIoPresent: false,
            discoveryError: null);

        var viewModel = new MainViewModel(session, mapped.Identity, environment);

        Assert.Equal(
            "Safe fan control is ready. Motherboard fans stay on BIOS until you run a test or press Optimize. Close other fan apps first if they appear below.",
            viewModel.Banner);
        Assert.Equal("Observation from this PC stored in memory.", viewModel.SessionStatusText);
        Assert.Equal("ASUS ROG STRIX Z790-E", viewModel.HardwareDisplayName);
        Assert.Equal("Intel Core i7-13700K", viewModel.CpuName);
        Assert.Equal("NVIDIA GeForce RTX 4070", viewModel.GpuName);
        Assert.True(viewModel.IsSetupBlocked);
        Assert.True(viewModel.IsHomeTab);
        Assert.True(viewModel.ShowHomeFans);
        Assert.False(viewModel.CanOptimize);
        Assert.False(viewModel.CanEditPriorities);
        Assert.Contains(viewModel.EnvironmentChecks, row => row.Title == "Administrator" && row.Status == "Missing" && !row.Passed);
        Assert.Contains(viewModel.EnvironmentChecks, row => row.Title == "PawnIO" && row.Status == "Missing" && !row.Passed);
        Assert.Contains(
            viewModel.EnvironmentChecks,
            row => row.Title == FanPresenceRunner.Title && row.Status == "Needed" && !row.Passed && !row.CanRun);
        Assert.Contains(viewModel.Temperatures, row => row.Name == "CPU Package" && row.Value.Contains("48", StringComparison.Ordinal));
        Assert.Contains(viewModel.Temperatures, row => row.Name == "Chipset" && row.Value == "unknown");
        Assert.Contains(viewModel.PowerReadings, row => row.Name == "GPU Package");
        Assert.Contains(viewModel.FanGroups, row => row.Name == "Pump" && row.Kind == "Pump" && row.ControlNote == "Pump (not written)");
        Assert.Contains(viewModel.FanGroups, row => row.Name == "Fan #1" && row.ControlNote == "Controllable");
        Assert.Contains(viewModel.EnvironmentChecks, row => row.Title == EnvironmentStatus.CompetingSoftwareTitle && row.Status == "OK");
        Assert.Contains(viewModel.FanGroups, row => row.Name == "Fan #3" && row.Duty == "unknown" && row.ControlNote == "Read-only");
        Assert.True(viewModel.CanRunBaseline);
        Assert.False(viewModel.IsBaselineRunning);
        Assert.False(viewModel.HasBaselineResults);
        Assert.True(viewModel.CanRunFanTests);
        Assert.False(viewModel.IsFanTestRunning);
        Assert.False(viewModel.HasFanTestResults);
        Assert.True(viewModel.CanRunInteractions);
        Assert.False(viewModel.IsInteractionRunning);
        Assert.False(viewModel.HasInteractionResults);
        Assert.True(viewModel.CanBuildModel);
        Assert.False(viewModel.HasModelResults);
        Assert.False(viewModel.HasDiminishingReturnsResults);
        Assert.Contains(
            "several fan speeds",
            viewModel.DiminishingReturnsProgressText,
            StringComparison.Ordinal);
        Assert.False(viewModel.CanOptimize);
    }

    [Fact]
    public void FinishBaseline_labels_measured_and_unknown_metrics()
    {
        MappedHardware mapped = SensorTreeMapperTests.MapFixture();
        var session = new DummySessionResult(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DummySessionStatus.Observed,
            mapped.Snapshot,
            Detail: null);
        EnvironmentStatus environment = EnvironmentStatus.Create(
            isAdministrator: true,
            pawnIoPresent: true,
            discoveryError: null);
        var viewModel = new MainViewModel(session, mapped.Identity, environment, ReadyPreferences());
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var run = new BaselineRun(
            Guid.NewGuid(),
            at,
            at.AddMinutes(4),
            BaselineRunStatus.Completed,
            AbortDetail: null,
            AmbientCelsius: null,
            GpuLoadAvailable: false,
            [
                new BaselineSample(at, BaselinePhase.Idle, mapped.Snapshot),
            ],
            [
                new BaselineMetric(BaselineMetricNames.AmbientCelsius, null, "°C", MetricEvidence.Unknown),
                new BaselineMetric(BaselineMetricNames.CpuEverydayRiseCelsius, 4.2, "°C", MetricEvidence.Measured),
                new BaselineMetric(BaselineMetricNames.GpuEverydayRiseCelsius, null, "°C", MetricEvidence.Unknown),
                new BaselineMetric(BaselineMetricNames.CpuRiseCelsius, 12.4, "°C", MetricEvidence.Measured),
                new BaselineMetric(BaselineMetricNames.GpuRiseCelsius, null, "°C", MetricEvidence.Unknown),
                new BaselineMetric(BaselineMetricNames.CpuDecayCelsius, 8.1, "°C", MetricEvidence.Measured),
                new BaselineMetric(BaselineMetricNames.GpuDecayCelsius, null, "°C", MetricEvidence.Unknown),
                new BaselineMetric(BaselineMetricNames.CpuSettleSeconds, 10, "s", MetricEvidence.Measured),
                new BaselineMetric(BaselineMetricNames.GpuSettleSeconds, null, "s", MetricEvidence.Unknown),
            ]);

        viewModel.BeginBaseline();
        viewModel.FinishBaseline(run);

        Assert.True(viewModel.CanRunBaseline);
        Assert.False(viewModel.IsBaselineRunning);
        Assert.Contains(viewModel.BaselineResults, row => row.Name == "Ambient" && row.Value == "unknown");
        Assert.Contains(viewModel.BaselineResults, row => row.Name == "CPU everyday rise" && row.Value.Contains("Measured", StringComparison.Ordinal));
        Assert.Contains(viewModel.BaselineResults, row => row.Name == "CPU rise" && row.Value.Contains("Measured", StringComparison.Ordinal));
        Assert.Contains(viewModel.BaselineResults, row => row.Name == "GPU load available" && row.Value.Contains("CPU-only", StringComparison.Ordinal));
        Assert.Contains(viewModel.BaselineResults, row => row.Name == "CPU clock (last)");
        Assert.True(viewModel.CanRunFanTests);
    }

    [Fact]
    public void FinishFanTest_labels_measured_none_unknown_and_skipped()
    {
        MappedHardware mapped = SensorTreeMapperTests.MapFixture();
        var session = new DummySessionResult(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DummySessionStatus.Observed,
            mapped.Snapshot,
            Detail: null);
        EnvironmentStatus environment = EnvironmentStatus.Create(
            isAdministrator: true,
            pawnIoPresent: true,
            discoveryError: null);
        var viewModel = new MainViewModel(session, mapped.Identity, environment, ReadyPreferences());
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var run = new FanTestRun(
            Guid.NewGuid(),
            at,
            at.AddMinutes(6),
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            [
                new InfluenceEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    InfluenceTarget.Gpu,
                    6.5,
                    InfluenceEffect.VeryHigh,
                    MetricEvidence.Measured,
                    35,
                    60,
                    490,
                    840),
                new InfluenceEntry(
                    FakeHardwareBackend.RearFanId,
                    "Rear exhaust",
                    InfluenceTarget.Cpu,
                    0.2,
                    InfluenceEffect.None,
                    MetricEvidence.Measured,
                    30,
                    55,
                    360,
                    660),
                new InfluenceEntry(
                    FakeHardwareBackend.TopFanId,
                    "Top exhaust",
                    InfluenceTarget.Case,
                    null,
                    Effect: null,
                    MetricEvidence.Unknown,
                    25,
                    50,
                    275,
                    550),
            ],
            [
                new SkippedFanGroup(FakeHardwareBackend.PumpId, "AIO pump", FanTestReasons.Pump),
            ]);

        viewModel.BeginFanTest();
        Assert.False(viewModel.CanRunBaseline);
        viewModel.FinishFanTest(run);

        Assert.True(viewModel.CanRunFanTests);
        Assert.False(viewModel.IsFanTestRunning);
        Assert.True(viewModel.CanRunBaseline);
        Assert.True(viewModel.CanOptimize);
        Assert.Contains(
            viewModel.FanTestResults,
            row => row.Name == "Front intake on the GPU"
                && row.Value.Contains("6.5 °C cooler", StringComparison.Ordinal)
                && row.Value.Contains("very high", StringComparison.Ordinal)
                && row.Value.Contains("Measured", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.FanTestResults,
            row => row.Name == "Rear exhaust on the CPU"
                && row.Value.Contains("none", StringComparison.Ordinal)
                && row.Value.Contains("Measured", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.FanTestResults,
            row => row.Name == "Top exhaust on the Case" && row.Value == "unknown");
        Assert.Contains(
            viewModel.FanTestResults,
            row => row.Name == "AIO pump" && row.Value.Contains("Not tested", StringComparison.Ordinal));
    }

    [Fact]
    public void FinishInteraction_labels_measured_unknown_and_inferred()
    {
        MappedHardware mapped = SensorTreeMapperTests.MapFixture();
        var session = new DummySessionResult(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DummySessionStatus.Observed,
            mapped.Snapshot,
            Detail: null);
        EnvironmentStatus environment = EnvironmentStatus.Create(
            isAdministrator: true,
            pawnIoPresent: true,
            discoveryError: null);
        var viewModel = new MainViewModel(session, mapped.Identity, environment, ReadyPreferences());
        DateTimeOffset started = DateTimeOffset.UtcNow;
        var run = new InteractionRun(
            Guid.NewGuid(),
            started,
            started,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            [
                new InteractionEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    FakeHardwareBackend.TopFanId,
                    "Top exhaust",
                    InfluenceTarget.Gpu,
                    1.5,
                    0.7,
                    3.8,
                    1.6,
                    MetricEvidence.Measured,
                    InteractionBuilder.InferNote(1.6, InfluenceTarget.Gpu)),
                new InteractionEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    FakeHardwareBackend.TopFanId,
                    "Top exhaust",
                    InfluenceTarget.Case,
                    null,
                    null,
                    null,
                    null,
                    MetricEvidence.Unknown,
                    InferredNote: null),
            ],
            [
                new SkippedFanGroup(FakeHardwareBackend.PumpId, "AIO pump", FanTestReasons.Pump),
            ]);

        viewModel.BeginInteraction();
        Assert.False(viewModel.CanRunBaseline);
        Assert.False(viewModel.CanRunFanTests);
        viewModel.FinishInteraction(run);

        Assert.True(viewModel.CanRunInteractions);
        Assert.False(viewModel.IsInteractionRunning);
        Assert.True(viewModel.CanRunBaseline);
        Assert.True(viewModel.CanRunFanTests);
        Assert.True(viewModel.CanOptimize);
        Assert.Contains(
            viewModel.InteractionResults,
            row => row.Name.Contains("Front intake by itself", StringComparison.Ordinal)
                && row.Value.Contains("1.5 °C cooler", StringComparison.Ordinal)
                && row.Value.Contains("Measured", StringComparison.Ordinal)
                && !row.Value.Contains("Inferred", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.InteractionResults,
            row => row.Name.Contains("leftover vs adding them", StringComparison.Ordinal)
                && row.Value.Contains("Measured", StringComparison.Ordinal)
                && !row.Value.Contains("Inferred", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.InteractionResults,
            row => row.Name.Contains("on the Case, no sensor", StringComparison.Ordinal) && row.Value == "unknown");
        Assert.Contains(
            viewModel.InteractionResults,
            row => row.Name.Contains("on the GPU, possible meaning", StringComparison.Ordinal)
                && row.Value.Contains("Inferred", StringComparison.Ordinal)
                && row.Value.Contains("GPU", StringComparison.Ordinal)
                && !row.Value.Contains("Measured", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.InteractionResults,
            row => row.Name == "AIO pump" && row.Value.Contains("Not tested", StringComparison.Ordinal));
        Assert.Contains(viewModel.InteractionResults, row => row.Name == "How to read this");
        Assert.Contains("Finished", viewModel.InteractionProgressText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportInteraction_keeps_plain_status_and_live_temps()
    {
        MappedHardware mapped = SensorTreeMapperTests.MapFixture();
        var session = new DummySessionResult(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DummySessionStatus.Observed,
            mapped.Snapshot,
            Detail: null);
        EnvironmentStatus environment = EnvironmentStatus.Create(
            isAdministrator: true,
            pawnIoPresent: true,
            discoveryError: null);
        var viewModel = new MainViewModel(session, mapped.Identity, environment);

        viewModel.BeginInteraction();
        viewModel.ReportInteraction(new InteractionProgress(
            mapped.Snapshot,
            "Speeding up CPU Fan only. It goes back to BIOS after. Pair 1 of 1. About 75 seconds."));

        Assert.Contains("Speeding up CPU Fan only", viewModel.InteractionProgressText, StringComparison.Ordinal);
        Assert.Contains("CPU", viewModel.InteractionProgressText, StringComparison.Ordinal);
        Assert.Contains("GPU", viewModel.InteractionProgressText, StringComparison.Ordinal);
        Assert.DoesNotContain("Holding BIOS", viewModel.InteractionProgressText, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_discovery_error_and_ok_environment_checks()
    {
        MappedHardware mapped = SensorTreeMapperTests.MapFixture();
        var session = new DummySessionResult(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DummySessionStatus.Observed,
            mapped.Snapshot,
            Detail: null);
        EnvironmentStatus environment = EnvironmentStatus.Create(
            isAdministrator: true,
            pawnIoPresent: true,
            discoveryError: "Computer.Open failed");

        var viewModel = new MainViewModel(session, mapped.Identity, environment);

        Assert.True(viewModel.HasDiscoveryError);
        Assert.Equal("Computer.Open failed", viewModel.DiscoveryError);
        Assert.Contains(viewModel.EnvironmentChecks, row => row.Title == "Administrator" && row.Status == "OK");
        Assert.Contains(viewModel.EnvironmentChecks, row => row.Title == "PawnIO" && row.Status == "OK");
        Assert.Contains(viewModel.EnvironmentChecks, row => row.Title == EnvironmentStatus.CompetingSoftwareTitle && row.Status == "OK");
    }

    [Fact]
    public void Shows_competing_software_as_a_conflict()
    {
        MappedHardware mapped = SensorTreeMapperTests.MapFixture();
        var session = new DummySessionResult(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DummySessionStatus.Observed,
            mapped.Snapshot,
            Detail: null);
        EnvironmentStatus environment = EnvironmentStatus.Create(
            isAdministrator: true,
            pawnIoPresent: true,
            discoveryError: null,
            competingSoftware: ["FanControl", "iCUE"]);

        var viewModel = new MainViewModel(session, mapped.Identity, environment);

        Assert.Contains(
            viewModel.EnvironmentChecks,
            row => row.Title == EnvironmentStatus.CompetingSoftwareTitle
                && row.Status == "Conflict"
                && row.Detail.Contains("FanControl", StringComparison.Ordinal)
                && row.Detail.Contains("iCUE", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowModel_without_fan_tests_asks_to_run_them_first()
    {
        MainViewModel viewModel = CreateViewModel();
        ThermalModel model = ThermalModelFitter.Fit(
            new HardwareIdentity(
                "ASUS ROG STRIX Z790-E",
                "Intel Core i7-13700K",
                "NVIDIA GeForce RTX 4070",
                "ASUS ROG STRIX Z790-E"),
            baseline: null,
            fanTest: null,
            interaction: null);

        viewModel.ShowModel(model);

        Assert.True(viewModel.HasModelResults);
        Assert.True(viewModel.CanBuildModel);
        Assert.True(viewModel.CanOptimize);
        Assert.Contains("Optimize will observe", viewModel.OptimizeProgressText, StringComparison.Ordinal);
        Assert.Contains("Run fan tests first", viewModel.ModelProgressText, StringComparison.Ordinal);
        Assert.Contains(viewModel.ModelResults, row => row.Name == "How to read this");
        Assert.Contains(
            viewModel.ModelResults,
            row => row.Name == "This model is for"
                && row.Value.Contains("Intel Core i7-13700K", StringComparison.Ordinal)
                && row.Value.Contains("NVIDIA GeForce RTX 4070", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.ModelResults,
            row => row.Name == "Confidence"
                && row.Value.Contains("none", StringComparison.Ordinal)
                && row.Value.Contains("Run fan tests first", StringComparison.Ordinal));
        Assert.DoesNotContain(
            viewModel.ModelResults,
            row => row.Value.Contains("°C", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowModel_labels_measured_relationships_and_modeled_predictions()
    {
        MainViewModel viewModel = CreateViewModel();
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var fanTest = new FanTestRun(
            Guid.NewGuid(),
            at,
            at,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            [
                new InfluenceEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    InfluenceTarget.Gpu,
                    1.5,
                    InfluenceEffect.Low,
                    MetricEvidence.Measured,
                    35,
                    60,
                    490,
                    840),
                new InfluenceEntry(
                    FakeHardwareBackend.TopFanId,
                    "Top exhaust",
                    InfluenceTarget.Gpu,
                    0.7,
                    InfluenceEffect.Low,
                    MetricEvidence.Measured,
                    25,
                    50,
                    275,
                    550),
            ],
            []);
        var interaction = new InteractionRun(
            Guid.NewGuid(),
            at,
            at,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            [
                new InteractionEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    FakeHardwareBackend.TopFanId,
                    "Top exhaust",
                    InfluenceTarget.Gpu,
                    1.5,
                    0.7,
                    3.8,
                    1.6,
                    MetricEvidence.Measured,
                    InferredNote: null),
            ],
            []);
        ThermalModel model = ThermalModelFitter.Fit(
            new HardwareIdentity(
                "ASUS ROG STRIX Z790-E",
                "Intel Core i7-13700K",
                "NVIDIA GeForce RTX 4070",
                "ASUS ROG STRIX Z790-E"),
            baseline: null,
            fanTest,
            interaction);

        viewModel.ShowModel(model);

        Assert.True(viewModel.CanOptimize);
        Assert.Equal("Optimize", viewModel.OptimizeButtonText);
        Assert.Contains("Ready", viewModel.OptimizeProgressText, StringComparison.Ordinal);
        Assert.Contains("medium", viewModel.ModelProgressText, StringComparison.Ordinal);
        Assert.Contains(
            viewModel.ModelResults,
            row => row.Name.Contains("Front intake slightly affects GPU", StringComparison.Ordinal)
                && row.Value == "Measured");
        Assert.Contains(
            viewModel.ModelResults,
            row => row.Name == "Front intake on the GPU"
                && row.Value.Contains("1.5 °C cooler", StringComparison.Ordinal)
                && row.Value.Contains("Modeled", StringComparison.Ordinal)
                && !row.Value.Contains("Measured", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.ModelResults,
            row => row.Name.Contains("Front intake and Top exhaust on the GPU", StringComparison.Ordinal)
                && row.Value.Contains("3.8 °C cooler", StringComparison.Ordinal)
                && row.Value.Contains("Modeled", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.ModelResults,
            row => row.Name == "How to read this"
                && row.Value.Contains("Modeled", StringComparison.Ordinal)
                && row.Value.Contains("Measured", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowDiminishingReturns_without_a_curve_asks_for_several_speeds()
    {
        MainViewModel viewModel = CreateViewModel();

        viewModel.ShowDiminishingReturns(DiminishingReturnsAnalyzer.Analyze([]));

        Assert.True(viewModel.HasDiminishingReturnsResults);
        Assert.False(viewModel.HasRpmPlot);
        Assert.Equal(MeasuredRpmPlot.NeedFanTestsReason, viewModel.RpmPlot.EmptyReason);
        Assert.True(viewModel.CanOptimize);
        Assert.Contains(
            "several fan speeds",
            viewModel.DiminishingReturnsProgressText,
            StringComparison.Ordinal);
        Assert.Contains(viewModel.DiminishingReturnsResults, row => row.Name == "How to read this");
        Assert.DoesNotContain(
            viewModel.DiminishingReturnsResults,
            row => row.Value.Contains("RPM", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowDiminishingReturns_labels_measured_bands()
    {
        MainViewModel viewModel = CreateViewModel();
        FanSpeedCurve[] curves =
        [
            new FanSpeedCurve(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                InfluenceTarget.Cpu,
                [
                    new FanSpeedPoint(600, 74.2),
                    new FanSpeedPoint(700, 71.8),
                    new FanSpeedPoint(800, 70.5),
                    new FanSpeedPoint(900, 69.9),
                    new FanSpeedPoint(1000, 69.5),
                    new FanSpeedPoint(1200, 69.2),
                ],
                MetricEvidence.Measured),
        ];
        DiminishingReturnsReport report = DiminishingReturnsAnalyzer.Analyze(curves);

        viewModel.ShowDiminishingReturns(report, curves);

        Assert.True(viewModel.CanOptimize);
        Assert.True(viewModel.HasRpmPlot);
        Assert.Equal(6, viewModel.RpmPlot.Points.Count);
        Assert.Equal(900, viewModel.RpmPlot.RecommendedRpm);
        Assert.Equal(FakeHardwareBackend.FrontFanId, viewModel.SelectedRpmPlotFan?.FanGroupId);
        Assert.Equal(InfluenceTarget.Cpu, viewModel.SelectedRpmPlotTarget.Target);
        Assert.True(viewModel.IsCpuRpmPlotTarget);
        Assert.False(viewModel.IsGpuRpmPlotTarget);
        Assert.Contains("measured speed curve", viewModel.DiminishingReturnsProgressText, StringComparison.Ordinal);
        Assert.Contains(
            viewModel.DiminishingReturnsResults,
            row => row.Name.Contains("Front intake on the CPU, useful", StringComparison.Ordinal)
                && row.Value == "900–1000 RPM · Measured");
        Assert.Contains(
            viewModel.DiminishingReturnsResults,
            row => row.Name.Contains("wasted", StringComparison.Ordinal)
                && row.Value == "1200 RPM · Measured");
        Assert.Contains(
            viewModel.DiminishingReturnsResults,
            row => row.Name.Contains("recommended", StringComparison.Ordinal)
                && row.Value == "900 RPM · Measured");
    }

    [Fact]
    public void BeginFanTest_disables_build_model()
    {
        MainViewModel viewModel = CreateViewModel();

        viewModel.BeginFanTest();

        Assert.False(viewModel.CanBuildModel);
        Assert.False(viewModel.CanRunBaseline);
        Assert.False(viewModel.CanOptimize);
        Assert.Contains("Wait until the current test finishes", viewModel.OptimizeUnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowModel_after_aborted_fan_tests_keeps_optimize_off()
    {
        MainViewModel viewModel = CreateViewModel();
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var fanTest = new FanTestRun(
            Guid.NewGuid(),
            at,
            at,
            FanTestRunStatus.Aborted,
            "Stopped by the temperature limit.",
            GpuLoadAvailable: true,
            [],
            [
                new InfluenceEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    InfluenceTarget.Gpu,
                    1.5,
                    InfluenceEffect.Low,
                    MetricEvidence.Measured,
                    35,
                    60,
                    490,
                    840),
            ],
            []);
        ThermalModel model = ThermalModelFitter.Fit(
            new HardwareIdentity(
                "ASUS ROG STRIX Z790-E",
                "Intel Core i7-13700K",
                "NVIDIA GeForce RTX 4070",
                "ASUS ROG STRIX Z790-E"),
            baseline: null,
            fanTest,
            interaction: null);

        viewModel.ShowModel(model);

        Assert.True(viewModel.CanOptimize);
        Assert.Contains("Ready", viewModel.OptimizeProgressText, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowModel_then_optimize_stop_restores_other_actions()
    {
        MainViewModel viewModel = CreateViewModel();
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var fanTest = new FanTestRun(
            Guid.NewGuid(),
            at,
            at,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            [
                new InfluenceEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    InfluenceTarget.Gpu,
                    1.5,
                    InfluenceEffect.Low,
                    MetricEvidence.Measured,
                    35,
                    60,
                    490,
                    840),
            ],
            []);
        ThermalModel model = ThermalModelFitter.Fit(
            new HardwareIdentity(
                "ASUS ROG STRIX Z790-E",
                "Intel Core i7-13700K",
                "NVIDIA GeForce RTX 4070",
                "ASUS ROG STRIX Z790-E"),
            baseline: null,
            fanTest,
            interaction: null);

        viewModel.ShowModel(model);
        Assert.True(viewModel.CanOptimize);
        viewModel.BeginOptimize();
        Assert.True(viewModel.IsOptimizeRunning);
        Assert.True(viewModel.IsCharacterizing);
        Assert.False(viewModel.IsHoldingPolicy);
        Assert.Equal("Cancel", viewModel.OptimizeButtonText);
        Assert.False(viewModel.CanRunBaseline);
        Assert.False(viewModel.CanEditPriorities);
        viewModel.BeginHold();
        Assert.True(viewModel.IsHoldingPolicy);
        Assert.False(viewModel.IsCharacterizing);
        Assert.Equal("Stop", viewModel.OptimizeButtonText);
        viewModel.FinishOptimize();

        Assert.False(viewModel.IsOptimizeRunning);
        Assert.False(viewModel.IsHoldingPolicy);
        Assert.True(viewModel.IsHomeReady);
        Assert.True(viewModel.CanOptimize);
        Assert.Equal("Optimize", viewModel.OptimizeButtonText);
        Assert.True(viewModel.CanRunBaseline);
        Assert.True(viewModel.CanEditPriorities);
        Assert.Contains("back on BIOS", viewModel.OptimizeProgressText, StringComparison.Ordinal);
        Assert.True(viewModel.HasExplanationResults);
    }

    [Fact]
    public void ShowExplanation_suffixes_tags_on_every_row()
    {
        MainViewModel viewModel = CreateViewModel();
        ThermalModel model = ExplanationModel();
        DiminishingReturnsReport returns = DiminishingReturnsAnalyzer.Analyze([]);
        CoolingPolicy? policy = PolicyOptimizer.Recommend(model, returns, CoolingPreferences.Default);
        ExplanationReport report = ExplanationReportBuilder.Build(model, returns, policy);

        viewModel.ShowExplanation(report);

        Assert.True(viewModel.HasExplanationResults);
        Assert.Contains(
            viewModel.ExplanationResults,
            row => row.Name == "How to read this"
                && row.Value.Contains("Measured", StringComparison.Ordinal)
                && row.Value.Contains("Modeled", StringComparison.Ordinal)
                && row.Value.Contains("Inferred", StringComparison.Ordinal)
                && row.Value.Contains("Unknown", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.ExplanationResults,
            row => row.Name.Contains("Primary GPU cooling path", StringComparison.Ordinal)
                && row.Value.Contains("Inferred", StringComparison.Ordinal)
                && !row.Value.Contains("°C", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.ExplanationResults,
            row => row.Name.Contains("Most effective fan", StringComparison.Ordinal)
                && row.Value.Contains("Measured", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.ExplanationResults,
            row => row.Name.Contains("Recommended operating point", StringComparison.Ordinal)
                && row.Value.Contains("Modeled", StringComparison.Ordinal));
        Assert.Contains("does not change fans", viewModel.ExplanationProgressText, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowExplanation_empty_data_still_explains_unknown()
    {
        MainViewModel viewModel = CreateViewModel();
        ThermalModel model = ThermalModelFitter.Fit(
            new HardwareIdentity(
                "ASUS ROG STRIX Z790-E",
                "Intel Core i7-13700K",
                "NVIDIA GeForce RTX 4070",
                "ASUS ROG STRIX Z790-E"),
            baseline: null,
            fanTest: null,
            interaction: null);
        ExplanationReport report = ExplanationReportBuilder.Build(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            policy: null);

        viewModel.ShowExplanation(report);

        Assert.True(viewModel.HasExplanationResults);
        Assert.Contains(viewModel.ExplanationResults, row => row.Name == "How to read this");
        Assert.Contains(
            viewModel.ExplanationResults,
            row => row.Name.Contains("Primary GPU cooling path", StringComparison.Ordinal)
                && row.Value.Contains("Unknown", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.ExplanationResults,
            row => row.Name.Contains("Expected result", StringComparison.Ordinal)
                && row.Value.Contains("Unknown", StringComparison.Ordinal));
        Assert.DoesNotContain(
            viewModel.ExplanationResults,
            row => row.Value.Contains(" · Measured", StringComparison.Ordinal)
                || row.Value.Contains(" · Modeled", StringComparison.Ordinal)
                || row.Value.Contains(" · Inferred", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowModel_fills_explanation_and_stop_keeps_it()
    {
        MainViewModel viewModel = CreateViewModel();
        ThermalModel model = ExplanationModel();

        viewModel.ShowModel(model);

        Assert.True(viewModel.HasExplanationResults);
        Assert.Contains(
            viewModel.ExplanationResults,
            row => row.Name.Contains("Recommended operating point", StringComparison.Ordinal)
                && row.Value.Contains("Modeled", StringComparison.Ordinal));
        IReadOnlyList<SensorRow> before = viewModel.ExplanationResults;
        viewModel.BeginOptimize();
        viewModel.FinishOptimize();

        Assert.True(viewModel.HasExplanationResults);
        Assert.Equal(before.Count, viewModel.ExplanationResults.Count);
    }

    private static ThermalModel ExplanationModel()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var fanTest = new FanTestRun(
            Guid.NewGuid(),
            at,
            at,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            [
                new InfluenceEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    InfluenceTarget.Gpu,
                    1.5,
                    InfluenceEffect.Low,
                    MetricEvidence.Measured,
                    35,
                    60,
                    490,
                    840),
                new InfluenceEntry(
                    FakeHardwareBackend.TopFanId,
                    "Top exhaust",
                    InfluenceTarget.Gpu,
                    0.7,
                    InfluenceEffect.Low,
                    MetricEvidence.Measured,
                    25,
                    50,
                    275,
                    550),
            ],
            []);
        var interaction = new InteractionRun(
            Guid.NewGuid(),
            at,
            at,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            [
                new InteractionEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    FakeHardwareBackend.TopFanId,
                    "Top exhaust",
                    InfluenceTarget.Gpu,
                    1.5,
                    0.7,
                    3.8,
                    1.6,
                    MetricEvidence.Measured,
                    InteractionBuilder.InferNote(1.6, InfluenceTarget.Gpu)),
            ],
            []);
        return ThermalModelFitter.Fit(
            new HardwareIdentity(
                "ASUS ROG STRIX Z790-E",
                "Intel Core i7-13700K",
                "NVIDIA GeForce RTX 4070",
                "ASUS ROG STRIX Z790-E"),
            baseline: null,
            fanTest,
            interaction);
    }

    [Fact]
    public void UpdateLiveReadings_refreshes_temperature_and_power()
    {
        var hardware = new FakeHardwareBackend();
        DummySessionResult session = new DummySession(hardware, new InMemorySessionStore()).Run();
        EnvironmentStatus environment = EnvironmentStatus.Create(
            isAdministrator: true,
            pawnIoPresent: true,
            discoveryError: null);
        var viewModel = new MainViewModel(
            session,
            new HardwareIdentity(hardware.DisplayName, "cpu", "gpu", "mb"),
            environment);

        Assert.Contains(
            viewModel.Temperatures,
            row => row.Name == "CPU" && row.Value.Contains("45", StringComparison.Ordinal));

        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 77);
        hardware.OverrideReading(FakeHardwareBackend.CpuPowerId, 120);
        viewModel.UpdateLiveReadings(hardware.ReadSnapshot());

        Assert.Contains(
            viewModel.Temperatures,
            row => row.Name == "CPU" && row.Value.Contains("77", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.PowerReadings,
            row => row.Name == "CPU Package" && row.Value.Contains("120", StringComparison.Ordinal));
    }

    [Fact]
    public void RememberConfirmation_offers_recheck_until_ignored()
    {
        MainViewModel viewModel = CreateViewModel();
        ThermalModel model = ExplanationModel();
        viewModel.ShowModel(model);
        viewModel.ShowDiminishingReturns(DiminishingReturnsAnalyzer.Analyze([]));
        var confirmation = new PolicyConfirmation(70, 78, 70, 72, Missed: true, AddedAirflow: true);
        DriftAssessment drift = DriftDetector.Assess(confirmation, 22, model.Influence);

        viewModel.RememberConfirmation(confirmation, drift);

        Assert.True(viewModel.CanRetest);
        Assert.True(viewModel.RetestOfferVisible);
        Assert.NotEmpty(viewModel.RetestGroupIds);

        viewModel.IgnoreDriftOffer();

        Assert.False(viewModel.CanRetest);
        Assert.False(viewModel.RetestOfferVisible);
    }

    [Fact]
    public void SelectTab_switches_home_learned_and_advanced()
    {
        MainViewModel viewModel = CreateViewModel();

        viewModel.SelectTab(ShellTab.Advanced);
        Assert.True(viewModel.IsAdvancedTab);
        Assert.False(viewModel.IsHomeReady);
        Assert.False(viewModel.ShowHomeFans);

        viewModel.SelectTab(ShellTab.Learned);
        Assert.True(viewModel.IsLearnedTab);
        Assert.False(viewModel.IsAdvancedTab);

        viewModel.SelectTab(ShellTab.Home);
        Assert.True(viewModel.IsHomeTab);
        Assert.True(viewModel.IsHomeReady);
        Assert.True(viewModel.ShowHomeFans);

        viewModel.SelectTab(ShellTab.Learned);
        viewModel.BeginOptimize();
        Assert.True(viewModel.IsHomeTab);
    }

    [Fact]
    public void Clicking_the_gpu_name_picks_that_card_until_a_test_starts()
    {
        MainViewModel viewModel = CreateViewModel();
        viewModel.SetAvailableGpus(
        [
            new GpuDevice("/gpu/igpu", "Intel UHD Graphics 770", LooksDiscrete: false),
            new GpuDevice("/gpu/dgpu", "NVIDIA GeForce RTX 4070", LooksDiscrete: true),
        ]);

        Assert.True(viewModel.CanChooseGpu);
        Assert.Equal("NVIDIA GeForce RTX 4070", viewModel.GpuName);
        Assert.True(viewModel.ChooseGpu("/gpu/igpu"));
        Assert.Equal("Intel UHD Graphics 770", viewModel.GpuName);
        Assert.Equal("/gpu/igpu", viewModel.CurrentPreferences().PreferredGpuId);

        viewModel.BeginOptimize();
        Assert.False(viewModel.CanChooseGpu);
        Assert.False(viewModel.ChooseGpu("/gpu/dgpu"));
        Assert.Equal("Intel UHD Graphics 770", viewModel.GpuName);
    }

    [Fact]
    public void Hide_unused_drops_headers_with_no_tach()
    {
        MainViewModel viewModel = CreateViewModel(CoolingPreferences.Default);
        var snapshot = new HardwareSnapshot(
            DateTimeOffset.UtcNow,
            [],
            [
                new FanGroup("/fan/used", "CPU Fan", 40, 980, "Nuvoton", true),
                new FanGroup("/fan/empty", "Fan #7", 50, 0, "Nuvoton", true),
                new FanGroup("/fan/none", "Fan #8", null, null, "Nuvoton", false),
            ],
            IsDemoHardware: false);
        viewModel.UpdateLiveReadings(snapshot);

        Assert.True(viewModel.CanHideUnusedFans);
        Assert.Equal("Hide unused", viewModel.UnusedFansButtonText);
        Assert.Equal(3, viewModel.FanGroups.Count);

        viewModel.ToggleUnusedFans();

        Assert.True(viewModel.HideDisconnectedFans);
        Assert.Equal("Show unused", viewModel.UnusedFansButtonText);
        Assert.Equal("CPU Fan", Assert.Single(viewModel.FanGroups).Name);
        Assert.True(viewModel.CurrentPreferences().HideDisconnectedFans);

        viewModel.ToggleUnusedFans();
        Assert.Equal(3, viewModel.FanGroups.Count);
    }

    [Fact]
    public void Hide_unused_keeps_gpu_fans_that_are_stopped()
    {
        MainViewModel viewModel = CreateViewModel(CoolingPreferences.Default);
        var snapshot = new HardwareSnapshot(
            DateTimeOffset.UtcNow,
            [],
            [
                new FanGroup("/fan/empty", "Fan #7", 50, 0, "Nuvoton", true),
                new FanGroup("/gpu/0/control/0", "GPU Fan", 0, 0, "NVIDIA GeForce RTX 5070 Ti", true),
            ],
            IsDemoHardware: false);
        viewModel.UpdateLiveReadings(snapshot);
        viewModel.ToggleUnusedFans();

        Assert.Equal("GPU Fan", Assert.Single(viewModel.FanGroups).Name);
        Assert.Contains("0 RPM", viewModel.FanGroups[0].Rpm, StringComparison.Ordinal);
    }

    [Fact]
    public void Hide_unused_hides_gpu_only_after_detect_says_empty()
    {
        var preferences = new CoolingPreferences(
            0.5,
            80,
            75,
            HideDisconnectedFans: true,
            Presence: new FanPresence(true, ["/fan/used"], ["/gpu/0/control/0"]));
        MainViewModel viewModel = CreateViewModel(preferences);
        var snapshot = new HardwareSnapshot(
            DateTimeOffset.UtcNow,
            [],
            [
                new FanGroup("/fan/used", "CPU Fan", 40, 980, "Nuvoton", true),
                new FanGroup("/gpu/0/control/0", "GPU Fan", 0, 0, "NVIDIA GeForce RTX 5070 Ti", true),
            ],
            IsDemoHardware: false);
        viewModel.UpdateLiveReadings(snapshot);

        Assert.Equal("CPU Fan", Assert.Single(viewModel.FanGroups).Name);
    }

    [Fact]
    public void Finding_connected_fans_turns_the_check_green_and_hides_empty_headers()
    {
        MainViewModel viewModel = CreateViewModel(CoolingPreferences.Default);
        var snapshot = new HardwareSnapshot(
            DateTimeOffset.UtcNow,
            [],
            [
                new FanGroup("/fan/used", "CPU Fan", 40, 980, "Nuvoton", true),
                new FanGroup("/fan/idle", "Fan #7", 0, 0, "Nuvoton", true),
                new FanGroup("/fan/empty", "Fan #8", 50, 0, "Nuvoton", true),
                new FanGroup("/gpu/0/control/0", "GPU Fan", 0, 0, "NVIDIA GeForce RTX 5070 Ti", true),
            ],
            IsDemoHardware: false);
        viewModel.UpdateLiveReadings(snapshot);

        Assert.True(viewModel.IsSetupBlocked);
        Assert.False(viewModel.CanOptimize);
        Assert.True(viewModel.CanRunFanPresence);
        Assert.Contains(
            viewModel.EnvironmentChecks,
            row => row.Title == FanPresenceRunner.Title && row.Status == "Needed" && !row.Passed && row.CanRun);

        viewModel.BeginFanPresence();
        Assert.False(viewModel.CanRunFanPresence);
        Assert.Contains(
            viewModel.EnvironmentChecks,
            row => row.Title == FanPresenceRunner.Title && row.Status == "Checking" && !row.Passed);

        viewModel.FinishFanPresence(new FanPresenceReport(
            FanPresenceStatus.Completed,
            ["/fan/used", "/fan/idle"],
            ["/fan/empty"],
            [],
            Detail: null));

        Assert.False(viewModel.IsSetupBlocked);
        Assert.True(viewModel.CanOptimize);
        Assert.True(viewModel.HideDisconnectedFans);
        Assert.Contains(
            viewModel.EnvironmentChecks,
            row => row.Title == FanPresenceRunner.Title && row.Status == "OK" && row.Passed);
        Assert.Contains(viewModel.FanGroups, row => row.Name == "CPU Fan");
        Assert.Contains(viewModel.FanGroups, row => row.Name == "Fan #7");
        Assert.Contains(viewModel.FanGroups, row => row.Name == "GPU Fan");
        Assert.DoesNotContain(viewModel.FanGroups, row => row.Name == "Fan #8");
        Assert.True(viewModel.CurrentPreferences().ConnectedFans.Completed);
        Assert.Equal(["/fan/empty"], viewModel.CurrentPreferences().ConnectedFans.EmptyIds);
    }

    [Fact]
    public void Hide_unused_keeps_a_spinning_hub_even_if_an_old_detect_marked_it_empty()
    {
        var preferences = new CoolingPreferences(
            0.5,
            80,
            75,
            HideDisconnectedFans: true,
            Presence: new FanPresence(true, ["/fan/cpu"], ["/lpc/sys1"]));
        MainViewModel viewModel = CreateViewModel(preferences);
        var snapshot = new HardwareSnapshot(
            DateTimeOffset.UtcNow,
            [],
            [
                new FanGroup("/fan/cpu", "CPU Fan", 40, 980, "Nuvoton", true),
                new FanGroup("/lpc/sys1", "System Fan #1", 70, 1100, "Nuvoton", true),
            ],
            IsDemoHardware: false);
        viewModel.UpdateLiveReadings(snapshot);

        Assert.Contains(viewModel.FanGroups, row => row.Name == "System Fan #1");
        Assert.Contains(viewModel.FanGroups, row => row.Name == "CPU Fan");
    }

    private static CoolingPreferences ReadyPreferences() =>
        CoolingPreferences.Default with { Presence = FanPresence.Ready };

    private static MainViewModel CreateViewModel(CoolingPreferences? preferences = null)
    {
        MappedHardware mapped = SensorTreeMapperTests.MapFixture();
        var session = new DummySessionResult(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DummySessionStatus.Observed,
            mapped.Snapshot,
            Detail: null);
        EnvironmentStatus environment = EnvironmentStatus.Create(
            isAdministrator: true,
            pawnIoPresent: true,
            discoveryError: null);
        return new MainViewModel(session, mapped.Identity, environment, preferences ?? ReadyPreferences());
    }
}
