using System.ComponentModel;
using AutoFan.Core;

namespace AutoFan.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private ThermalModel? _model;
    private DiminishingReturnsReport _returns = new([]);
    private IReadOnlyList<FanSpeedCurve> _speedCurves = [];
    private string? _selectedRpmPlotFanId;
    private InfluenceTarget _selectedRpmPlotTarget = InfluenceTarget.Cpu;
    private PolicyConfirmation? _confirmation;
    private DriftAssessment? _drift;
    private double _quietCool;
    private double _cpuTargetCelsius;
    private double _gpuTargetCelsius;
    private double _cpuAbortCelsius;
    private double _gpuAbortCelsius;
    private string? _preferredGpuId;
    private bool _hideDisconnectedFans;
    private FanPresence _presence;
    private string _presenceDetail = FanPresenceRunner.NeededDetail;
    private readonly IReadOnlyList<CheckRow> _baseChecks;
    private HardwareSnapshot? _lastSnapshot;
    private IReadOnlyList<FanRow> _allFanGroups = [];

    public MainViewModel(
        DummySessionResult session,
        HardwareIdentity identity,
        EnvironmentStatus environment,
        CoolingPreferences? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(environment);

        WindowTitle = "AUTO Fan";
        HardwareDisplayName = identity.DisplayName;
        CpuName = identity.CpuName ?? "unknown";
        GpuName = identity.GpuName ?? "unknown";
        MotherboardName = identity.MotherboardName ?? "unknown";
        IsDemoHardware = session.Snapshot.IsDemoHardware;
        Banner = IsDemoHardware
            ? "Foundation build. These readings are from demo hardware, not your PC."
            : "Safe fan control is ready. Motherboard fans stay on BIOS until you run a test or press Optimize. Close other fan apps first if they appear below.";
        DiscoveryError = environment.DiscoveryError;
        SessionStatusText = DescribeStatus(session);
        CoolingPreferences prefs = preferences ?? CoolingPreferences.Default;
        _preferredGpuId = prefs.PreferredGpuId;
        _hideDisconnectedFans = prefs.HideDisconnectedFans;
        _presence = prefs.ConnectedFans;
        if (_presence.Completed)
        {
            _presenceDetail = FanPresenceRunner.DescribeSuccess(
                _presence.ConnectedIds.Count,
                _presence.EmptyIds.Count);
        }
        _quietCool = prefs.Blend;
        _cpuAbortCelsius = prefs.CpuAbort;
        _gpuAbortCelsius = prefs.GpuAbort;
        _cpuTargetCelsius = prefs.CpuTarget;
        _gpuTargetCelsius = prefs.GpuTarget;
        CanOptimize = true;
        OptimizeUnavailableReason = string.Empty;
        OptimizeButtonText = "Optimize";
        OptimizeProgressText = "Ready. Optimize will observe, test fans, then apply a setting.";
        CanEditPriorities = true;
        HomeTagline =
            "Test this PC, then hold a quieter or cooler fan policy while this window is open.";
        SetupHeadline = "This PC isn't ready to test yet.";
        BiosCaption = "Stop, close, or a temperature limit puts fans back on BIOS and the NVIDIA driver.";
        HoldingHeadline = "Holding a policy for this PC";
        HoldingCaption =
            "Motherboard and NVIDIA fans follow AUTO Fan while this window is open. Close the window to return to BIOS and the driver.";
        _baseChecks = environment.Checks
            .Select(static check => new CheckRow(
                check.Title,
                StatusLabel(check),
                check.Detail,
                check.Passed))
            .ToArray();
        PublishEnvironmentChecks();
        BindLiveReadings(session.Snapshot);
        CanRunBaseline = true;
        IsBaselineRunning = false;
        BaselineProgressText = "Ready. This does not change fans.";
        BaselineResults = [];
        CanRunFanTests = true;
        IsFanTestRunning = false;
        FanTestProgressText = "Ready. Click Run fan tests when CPU and GPU are 60 °C or cooler.";
        FanTestResults = [];
        CanRunInteractions = true;
        IsInteractionRunning = false;
        InteractionProgressText = "Ready. Click Run interactions when CPU and GPU are 60 °C or cooler.";
        InteractionResults = [];
        CanBuildModel = true;
        ModelProgressText = "Ready. Uses stored tests. Does not change fans.";
        ModelResults = [];
        DiminishingReturnsProgressText =
            "Need a measured speed-versus-temperature curve (several fan speeds, not just one test step).";
        DiminishingReturnsResults = [];
        RpmPlotFans = [];
        RpmPlot = MeasuredRpmPlot.Empty(InfluenceTarget.Cpu, MeasuredRpmPlot.NeedFanTestsReason);
        ExplanationProgressText = "A writeup of stored tests. It does not change fans.";
        ExplanationResults = [];
        CanRetest = false;
        CanIgnoreDrift = false;
        SelectedTab = ShellTab.Home;
        IsHoldingPolicy = false;

        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(HardwareDisplayName));
        OnPropertyChanged(nameof(CpuName));
        OnPropertyChanged(nameof(GpuName));
        OnPropertyChanged(nameof(Gpus));
        OnPropertyChanged(nameof(CanChooseGpu));
        OnPropertyChanged(nameof(MotherboardName));
        OnPropertyChanged(nameof(Banner));
        OnPropertyChanged(nameof(IsDemoHardware));
        OnPropertyChanged(nameof(DiscoveryError));
        OnPropertyChanged(nameof(HasDiscoveryError));
        OnPropertyChanged(nameof(SessionStatusText));
        OnPropertyChanged(nameof(CanOptimize));
        OnPropertyChanged(nameof(OptimizeUnavailableReason));
        OnPropertyChanged(nameof(QuietCool));
        OnPropertyChanged(nameof(CpuTargetCelsius));
        OnPropertyChanged(nameof(GpuTargetCelsius));
        OnPropertyChanged(nameof(CpuAbortCelsius));
        OnPropertyChanged(nameof(GpuAbortCelsius));
        OnPropertyChanged(nameof(OptimizeButtonText));
        OnPropertyChanged(nameof(OptimizeProgressText));
        OnPropertyChanged(nameof(CanEditPriorities));
        OnPropertyChanged(nameof(IsOptimizeRunning));
        OnPropertyChanged(nameof(EnvironmentChecks));
        OnPropertyChanged(nameof(Temperatures));
        OnPropertyChanged(nameof(PowerReadings));
        OnPropertyChanged(nameof(FanGroups));
        OnPropertyChanged(nameof(CanHideUnusedFans));
        OnPropertyChanged(nameof(UnusedFansButtonText));
        OnPropertyChanged(nameof(CanRunBaseline));
        OnPropertyChanged(nameof(IsBaselineRunning));
        OnPropertyChanged(nameof(BaselineProgressText));
        OnPropertyChanged(nameof(BaselineResults));
        OnPropertyChanged(nameof(HasBaselineResults));
        OnPropertyChanged(nameof(CanRunFanTests));
        OnPropertyChanged(nameof(IsFanTestRunning));
        OnPropertyChanged(nameof(FanTestProgressText));
        OnPropertyChanged(nameof(FanTestResults));
        OnPropertyChanged(nameof(HasFanTestResults));
        OnPropertyChanged(nameof(CanRunInteractions));
        OnPropertyChanged(nameof(IsInteractionRunning));
        OnPropertyChanged(nameof(InteractionProgressText));
        OnPropertyChanged(nameof(InteractionResults));
        OnPropertyChanged(nameof(HasInteractionResults));
        OnPropertyChanged(nameof(CanBuildModel));
        OnPropertyChanged(nameof(ModelProgressText));
        OnPropertyChanged(nameof(ModelResults));
        OnPropertyChanged(nameof(HasModelResults));
        OnPropertyChanged(nameof(DiminishingReturnsProgressText));
        OnPropertyChanged(nameof(DiminishingReturnsResults));
        OnPropertyChanged(nameof(HasDiminishingReturnsResults));
        OnPropertyChanged(nameof(RpmPlotFans));
        OnPropertyChanged(nameof(HasRpmPlotFans));
        OnPropertyChanged(nameof(SelectedRpmPlotFan));
        OnPropertyChanged(nameof(SelectedRpmPlotTarget));
        OnPropertyChanged(nameof(IsCpuRpmPlotTarget));
        OnPropertyChanged(nameof(IsGpuRpmPlotTarget));
        OnPropertyChanged(nameof(RpmPlot));
        OnPropertyChanged(nameof(HasRpmPlot));
        OnPropertyChanged(nameof(ExplanationProgressText));
        OnPropertyChanged(nameof(ExplanationResults));
        OnPropertyChanged(nameof(HasExplanationResults));
        OnPropertyChanged(nameof(CanRetest));
        OnPropertyChanged(nameof(CanIgnoreDrift));
        OnPropertyChanged(nameof(RetestOfferVisible));
        OnPropertyChanged(nameof(HomeTagline));
        OnPropertyChanged(nameof(SetupHeadline));
        OnPropertyChanged(nameof(BiosCaption));
        OnPropertyChanged(nameof(HoldingHeadline));
        OnPropertyChanged(nameof(HoldingCaption));
        OnPropertyChanged(nameof(IsSetupBlocked));
        OnPropertyChanged(nameof(CpuTemperatureText));
        OnPropertyChanged(nameof(GpuTemperatureText));
        OnPropertyChanged(nameof(CpuPowerText));
        OnPropertyChanged(nameof(GpuPowerText));
        RefreshOptimizeAvailability();
        NotifyShell();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string WindowTitle { get; }

    public string HardwareDisplayName { get; }

    public string CpuName { get; }

    public string GpuName { get; private set; }

    public IReadOnlyList<GpuChoice> Gpus { get; private set; } = [];

    public bool CanChooseGpu =>
        Gpus.Count > 1
        && !IsOptimizeRunning
        && !IsBaselineRunning
        && !IsFanTestRunning
        && !IsInteractionRunning
        && !IsFanPresenceRunning;

    public string MotherboardName { get; }

    public string Banner { get; }

    public bool IsDemoHardware { get; }

    public string HomeTagline { get; }

    public string SetupHeadline { get; }

    public string BiosCaption { get; }

    public string HoldingHeadline { get; }

    public string HoldingCaption { get; }

    public bool IsSetupBlocked { get; private set; }

    public bool IsFanPresenceRunning { get; private set; }

    public bool CanRunFanPresence { get; private set; }

    public bool IsHoldingPolicy { get; private set; }

    public bool IsCharacterizing => IsOptimizeRunning && !IsHoldingPolicy;

    public bool IsHomeReady => IsHomeTab && !IsOptimizeRunning;

    public ShellTab SelectedTab { get; private set; }

    public bool IsHomeTab => SelectedTab == ShellTab.Home;

    public bool IsLearnedTab => SelectedTab == ShellTab.Learned;

    public bool IsAdvancedTab => SelectedTab == ShellTab.Advanced;

    public bool ShowHomeFans => IsHomeTab;

    public string CpuTemperatureText { get; private set; } = "unknown";

    public string GpuTemperatureText { get; private set; } = "unknown";

    public string CpuPowerText { get; private set; } = "unknown";

    public string GpuPowerText { get; private set; } = "unknown";

    public string? DiscoveryError { get; }

    public bool HasDiscoveryError => !string.IsNullOrWhiteSpace(DiscoveryError);

    public string SessionStatusText { get; }

    public bool CanOptimize { get; private set; }

    public string OptimizeUnavailableReason { get; private set; }

    public double QuietCool
    {
        get => _quietCool;
        set
        {
            double clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(_quietCool - clamped) < 0.0001)
            {
                return;
            }

            _quietCool = clamped;
            OnPropertyChanged(nameof(QuietCool));
            if (!IsOptimizeRunning)
            {
                RefreshOptimizeAvailability();
            }
        }
    }

    public double CpuTargetCelsius
    {
        get => _cpuTargetCelsius;
        set
        {
            double clamped = CoolingPreferences.ClampCpu(value, _cpuAbortCelsius);
            if (Math.Abs(_cpuTargetCelsius - clamped) < 0.0001)
            {
                return;
            }

            _cpuTargetCelsius = clamped;
            OnPropertyChanged(nameof(CpuTargetCelsius));
            if (!IsOptimizeRunning)
            {
                RefreshOptimizeAvailability();
            }
        }
    }

    public double GpuTargetCelsius
    {
        get => _gpuTargetCelsius;
        set
        {
            double clamped = CoolingPreferences.ClampGpu(value, _gpuAbortCelsius);
            if (Math.Abs(_gpuTargetCelsius - clamped) < 0.0001)
            {
                return;
            }

            _gpuTargetCelsius = clamped;
            OnPropertyChanged(nameof(GpuTargetCelsius));
            if (!IsOptimizeRunning)
            {
                RefreshOptimizeAvailability();
            }
        }
    }

    public double CpuAbortCelsius
    {
        get => _cpuAbortCelsius;
        set
        {
            double clamped = ThermalAbortLimits.ClampCpu(value);
            if (Math.Abs(_cpuAbortCelsius - clamped) < 0.0001)
            {
                return;
            }

            _cpuAbortCelsius = clamped;
            OnPropertyChanged(nameof(CpuAbortCelsius));
            CpuTargetCelsius = _cpuTargetCelsius;
            if (!IsOptimizeRunning)
            {
                RefreshOptimizeAvailability();
            }
        }
    }

    public double GpuAbortCelsius
    {
        get => _gpuAbortCelsius;
        set
        {
            double clamped = ThermalAbortLimits.ClampGpu(value);
            if (Math.Abs(_gpuAbortCelsius - clamped) < 0.0001)
            {
                return;
            }

            _gpuAbortCelsius = clamped;
            OnPropertyChanged(nameof(GpuAbortCelsius));
            GpuTargetCelsius = _gpuTargetCelsius;
            if (!IsOptimizeRunning)
            {
                RefreshOptimizeAvailability();
            }
        }
    }

    public bool CanEditPriorities { get; private set; }

    public bool IsOptimizeRunning { get; private set; }

    public string OptimizeButtonText { get; private set; }

    public string OptimizeProgressText { get; private set; }

    public bool CanRetest { get; private set; }

    public bool CanIgnoreDrift { get; private set; }

    public bool RetestOfferVisible => CanRetest || CanIgnoreDrift;

    public IReadOnlyList<string> RetestGroupIds => _drift?.RetestGroupIds ?? [];

    public bool HideDisconnectedFans => _hideDisconnectedFans;

    public bool CanHideUnusedFans => _allFanGroups.Any(static fan => fan.LooksUnused);

    public string UnusedFansButtonText =>
        _hideDisconnectedFans ? "Show unused" : "Hide unused";

    public CoolingPreferences CurrentPreferences() =>
        new(
            QuietCool,
            CpuTargetCelsius,
            GpuTargetCelsius,
            CpuAbortCelsius,
            GpuAbortCelsius,
            _preferredGpuId,
            _hideDisconnectedFans,
            _presence);

    public void BeginFanPresence()
    {
        CanRunBaseline = false;
        CanRunFanTests = false;
        CanRunInteractions = false;
        CanBuildModel = false;
        IsFanPresenceRunning = true;
        _presenceDetail = FanPresenceRunner.NeededDetail;
        PublishEnvironmentChecks();
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void ReportFanPresence(FanPresenceProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _presenceDetail = progress.Message;
        PublishEnvironmentChecks();
    }

    public void FinishFanPresence(FanPresenceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        IsFanPresenceRunning = false;
        CanRunBaseline = !IsFanTestRunning && !IsInteractionRunning && !IsOptimizeRunning;
        CanRunFanTests = !IsFanTestRunning && !IsInteractionRunning && !IsOptimizeRunning;
        CanRunInteractions = !IsInteractionRunning && !IsFanTestRunning && !IsOptimizeRunning;
        CanBuildModel = !IsFanTestRunning && !IsInteractionRunning && !IsOptimizeRunning;
        if (report.Status == FanPresenceStatus.Completed)
        {
            _presence = report.ToState();
            _presenceDetail = FanPresenceRunner.DescribeSuccess(
                report.ConnectedIds.Count,
                report.EmptyIds.Count);
            _hideDisconnectedFans = true;
            if (_lastSnapshot is HardwareSnapshot snapshot)
            {
                BindLiveReadings(snapshot);
            }
            else
            {
                PublishFanGroups();
            }
        }
        else if (!_presence.Completed)
        {
            _presenceDetail = report.Detail ?? "Could not finish finding connected fans. Fans are back on BIOS and the NVIDIA driver.";
        }

        PublishEnvironmentChecks();
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void FailFanPresence(string detail)
    {
        IsFanPresenceRunning = false;
        CanRunBaseline = !IsFanTestRunning && !IsInteractionRunning && !IsOptimizeRunning;
        CanRunFanTests = !IsFanTestRunning && !IsInteractionRunning && !IsOptimizeRunning;
        CanRunInteractions = !IsInteractionRunning && !IsFanTestRunning && !IsOptimizeRunning;
        CanBuildModel = !IsFanTestRunning && !IsInteractionRunning && !IsOptimizeRunning;
        if (!_presence.Completed)
        {
            _presenceDetail = string.IsNullOrWhiteSpace(detail)
                ? "Could not finish finding connected fans. Fans are back on BIOS and the NVIDIA driver."
                : detail;
        }

        PublishEnvironmentChecks();
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void ToggleUnusedFans()
    {
        if (!CanHideUnusedFans && !_hideDisconnectedFans)
        {
            return;
        }

        _hideDisconnectedFans = !_hideDisconnectedFans;
        PublishFanGroups();
    }

    public void SetAvailableGpus(IReadOnlyList<GpuDevice> gpus)
    {
        ArgumentNullException.ThrowIfNull(gpus);
        Gpus = gpus
            .Select(gpu => new GpuChoice(gpu.Id, gpu.Name, IsSelectedGpu(gpu)))
            .ToArray();
        GpuChoice? selected = Gpus.FirstOrDefault(static gpu => gpu.IsSelected)
            ?? Gpus.FirstOrDefault(static gpu => gpu.Name.Length > 0);
        if (selected is not null)
        {
            GpuName = selected.Name;
            _preferredGpuId ??= selected.Id;
        }

        OnPropertyChanged(nameof(Gpus));
        OnPropertyChanged(nameof(GpuName));
        OnPropertyChanged(nameof(CanChooseGpu));
    }

    public bool ChooseGpu(string hardwareId)
    {
        if (!CanChooseGpu || string.IsNullOrWhiteSpace(hardwareId))
        {
            return false;
        }

        GpuChoice? choice = Gpus.FirstOrDefault(gpu =>
            string.Equals(gpu.Id, hardwareId, StringComparison.Ordinal));
        if (choice is null)
        {
            return false;
        }

        _preferredGpuId = choice.Id;
        GpuName = choice.Name;
        Gpus = Gpus.Select(gpu => gpu with { IsSelected = gpu.Id == choice.Id }).ToArray();
        OnPropertyChanged(nameof(Gpus));
        OnPropertyChanged(nameof(GpuName));
        return true;
    }

    public void ApplyIdentity(HardwareIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!string.IsNullOrWhiteSpace(identity.GpuName))
        {
            GpuName = identity.GpuName;
            OnPropertyChanged(nameof(GpuName));
        }
    }

    public IReadOnlyList<CheckRow> EnvironmentChecks { get; private set; } = [];

    public IReadOnlyList<SensorRow> Temperatures { get; private set; } = [];

    public IReadOnlyList<SensorRow> PowerReadings { get; private set; } = [];

    public IReadOnlyList<FanRow> FanGroups { get; private set; } = [];

    public bool CanRunBaseline { get; private set; }

    public bool IsBaselineRunning { get; private set; }

    public string BaselineProgressText { get; private set; }

    public IReadOnlyList<SensorRow> BaselineResults { get; private set; }

    public bool HasBaselineResults => BaselineResults.Count > 0;

    public bool CanRunFanTests { get; private set; }

    public bool IsFanTestRunning { get; private set; }

    public string FanTestProgressText { get; private set; }

    public IReadOnlyList<SensorRow> FanTestResults { get; private set; }

    public bool HasFanTestResults => FanTestResults.Count > 0;

    public bool CanRunInteractions { get; private set; }

    public bool IsInteractionRunning { get; private set; }

    public string InteractionProgressText { get; private set; }

    public IReadOnlyList<SensorRow> InteractionResults { get; private set; }

    public bool HasInteractionResults => InteractionResults.Count > 0;

    public bool CanBuildModel { get; private set; }

    public string ModelProgressText { get; private set; }

    public IReadOnlyList<SensorRow> ModelResults { get; private set; }

    public bool HasModelResults => ModelResults.Count > 0;

    public string DiminishingReturnsProgressText { get; private set; }

    public IReadOnlyList<SensorRow> DiminishingReturnsResults { get; private set; }

    public bool HasDiminishingReturnsResults => DiminishingReturnsResults.Count > 0;

    public IReadOnlyList<MeasuredRpmFanChoice> RpmPlotFans { get; private set; }

    public bool HasRpmPlotFans => RpmPlotFans.Count > 0;

    public IReadOnlyList<MeasuredRpmTargetChoice> RpmPlotTargets => MeasuredRpmPlot.TargetChoices;

    public MeasuredRpmFanChoice? SelectedRpmPlotFan
    {
        get => RpmPlotFans.FirstOrDefault(fan =>
            string.Equals(fan.FanGroupId, _selectedRpmPlotFanId, StringComparison.Ordinal));
        set
        {
            if (value is null
                || string.Equals(_selectedRpmPlotFanId, value.FanGroupId, StringComparison.Ordinal))
            {
                return;
            }

            _selectedRpmPlotFanId = value.FanGroupId;
            RebuildRpmPlot();
            NotifyDiminishingReturns();
        }
    }

    public MeasuredRpmTargetChoice SelectedRpmPlotTarget
    {
        get => RpmPlotTargets.First(choice => choice.Target == _selectedRpmPlotTarget);
        set
        {
            if (value is null || value.Target == _selectedRpmPlotTarget)
            {
                return;
            }

            _selectedRpmPlotTarget = value.Target;
            RebuildRpmPlot();
            NotifyDiminishingReturns();
        }
    }

    public bool IsCpuRpmPlotTarget
    {
        get => _selectedRpmPlotTarget == InfluenceTarget.Cpu;
        set
        {
            if (!value || _selectedRpmPlotTarget == InfluenceTarget.Cpu)
            {
                return;
            }

            _selectedRpmPlotTarget = InfluenceTarget.Cpu;
            RebuildRpmPlot();
            NotifyDiminishingReturns();
        }
    }

    public bool IsGpuRpmPlotTarget
    {
        get => _selectedRpmPlotTarget == InfluenceTarget.Gpu;
        set
        {
            if (!value || _selectedRpmPlotTarget == InfluenceTarget.Gpu)
            {
                return;
            }

            _selectedRpmPlotTarget = InfluenceTarget.Gpu;
            RebuildRpmPlot();
            NotifyDiminishingReturns();
        }
    }

    public MeasuredRpmPlot RpmPlot { get; private set; }

    public bool HasRpmPlot => RpmPlot.HasPoints;

    public string ExplanationProgressText { get; private set; }

    public IReadOnlyList<SensorRow> ExplanationResults { get; private set; }

    public bool HasExplanationResults => ExplanationResults.Count > 0;

    public void BeginBaseline()
    {
        CanRunBaseline = false;
        CanRunFanTests = false;
        CanRunInteractions = false;
        CanBuildModel = false;
        IsBaselineRunning = true;
        BaselineProgressText = "Starting. Watching temperatures. Fans stay on BIOS.";
        BaselineResults = [];
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void ReportBaseline(BaselineProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        string cpu = FirstValue(progress.Latest, SensorKind.CpuTemperature, "°C");
        string gpu = FirstValue(progress.Latest, SensorKind.GpuTemperature, "°C");
        BaselineProgressText = $"{progress.Message} CPU {cpu}. GPU {gpu}.";
        OnPropertyChanged(nameof(BaselineProgressText));
    }

    public void FinishBaseline(BaselineRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        CanRunBaseline = true;
        CanRunFanTests = !IsFanTestRunning && !IsInteractionRunning;
        CanRunInteractions = !IsInteractionRunning && !IsFanTestRunning;
        IsBaselineRunning = false;
        BaselineProgressText = run.Status switch
        {
            BaselineRunStatus.Completed => "Finished. Fans were not changed. Measured = recorded. Unknown = no sensor.",
            BaselineRunStatus.Cancelled => "Cancelled. Fans were not changed.",
            BaselineRunStatus.Aborted => run.AbortDetail ?? "Stopped by the temperature limit. Fans were not changed.",
            _ => run.Status.ToString(),
        };
        BaselineResults = BuildResults(run);
        CanBuildModel = !IsFanTestRunning && !IsInteractionRunning;
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void FailBaseline(string detail)
    {
        CanRunBaseline = true;
        CanRunFanTests = !IsFanTestRunning && !IsInteractionRunning;
        CanRunInteractions = !IsInteractionRunning && !IsFanTestRunning;
        IsBaselineRunning = false;
        BaselineProgressText = string.IsNullOrWhiteSpace(detail)
            ? "Baseline failed."
            : detail;
        CanBuildModel = !IsFanTestRunning && !IsInteractionRunning;
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void BeginFanTest()
    {
        CanRunBaseline = false;
        CanRunFanTests = false;
        CanRunInteractions = false;
        CanBuildModel = false;
        IsFanTestRunning = true;
        FanTestProgressText = "Starting. Waiting until CPU and GPU are cool enough, then adding light heat.";
        FanTestResults = [];
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void ReportFanTest(FanTestProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        string cpu = FirstValue(progress.Latest, SensorKind.CpuTemperature, "°C");
        string gpu = FirstValue(progress.Latest, SensorKind.GpuTemperature, "°C");
        FanTestProgressText = $"{progress.Message} CPU {cpu}. GPU {gpu}.";
        OnPropertyChanged(nameof(FanTestProgressText));
    }

    public void FinishFanTest(FanTestRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        CanRunFanTests = true;
        CanRunBaseline = !IsBaselineRunning && !IsInteractionRunning;
        CanRunInteractions = !IsInteractionRunning && !IsBaselineRunning;
        IsFanTestRunning = false;
        FanTestProgressText = run.Status switch
        {
            FanTestRunStatus.Completed => "Finished. Fans are back on BIOS and the NVIDIA driver. Measured = recorded temperature change. Unknown = no sensor. Skipped = we did not change that fan.",
            FanTestRunStatus.Cancelled => "Cancelled. Fans are back on BIOS and the NVIDIA driver.",
            FanTestRunStatus.Aborted => run.AbortDetail ?? "Stopped by the temperature limit. Fans are back on BIOS and the NVIDIA driver.",
            _ => run.Status.ToString(),
        };
        FanTestResults = BuildFanTestResults(run);
        CanBuildModel = !IsBaselineRunning && !IsInteractionRunning;
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void FailFanTest(string detail)
    {
        CanRunFanTests = true;
        CanRunBaseline = !IsBaselineRunning && !IsInteractionRunning;
        CanRunInteractions = !IsInteractionRunning && !IsBaselineRunning;
        IsFanTestRunning = false;
        FanTestProgressText = string.IsNullOrWhiteSpace(detail)
            ? "Fan tests failed."
            : detail;
        CanBuildModel = !IsBaselineRunning && !IsInteractionRunning;
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void BeginInteraction()
    {
        CanRunBaseline = false;
        CanRunFanTests = false;
        CanRunInteractions = false;
        CanBuildModel = false;
        IsInteractionRunning = true;
        InteractionProgressText = "Starting. Waiting until CPU and GPU are cool enough, then adding light heat.";
        InteractionResults = [];
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void ReportInteraction(InteractionProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        string cpu = FirstValue(progress.Latest, SensorKind.CpuTemperature, "°C");
        string gpu = FirstValue(progress.Latest, SensorKind.GpuTemperature, "°C");
        InteractionProgressText = $"{progress.Message} CPU {cpu}. GPU {gpu}.";
        OnPropertyChanged(nameof(InteractionProgressText));
    }

    public void FinishInteraction(InteractionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        CanRunInteractions = true;
        CanRunBaseline = !IsBaselineRunning && !IsFanTestRunning;
        CanRunFanTests = !IsFanTestRunning && !IsBaselineRunning;
        IsInteractionRunning = false;
        InteractionProgressText = run.Status switch
        {
            FanTestRunStatus.Completed => "Finished. Fans are back on BIOS and the NVIDIA driver. Measured = recorded temperature change. Inferred = a guess. Unknown = no sensor. Skipped = we did not change that fan.",
            FanTestRunStatus.Cancelled => "Cancelled. Fans are back on BIOS and the NVIDIA driver.",
            FanTestRunStatus.Aborted => run.AbortDetail ?? "Stopped by the temperature limit. Fans are back on BIOS and the NVIDIA driver.",
            _ => run.Status.ToString(),
        };
        InteractionResults = BuildInteractionResults(run);
        CanBuildModel = !IsBaselineRunning && !IsFanTestRunning;
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void FailInteraction(string detail)
    {
        CanRunInteractions = true;
        CanRunBaseline = !IsBaselineRunning && !IsFanTestRunning;
        CanRunFanTests = !IsFanTestRunning && !IsBaselineRunning;
        IsInteractionRunning = false;
        InteractionProgressText = string.IsNullOrWhiteSpace(detail)
            ? "Interaction tests failed."
            : detail;
        CanBuildModel = !IsBaselineRunning && !IsFanTestRunning;
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void ShowModel(ThermalModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        ModelProgressText = model.Confidence == ModelConfidence.None
            ? model.ConfidenceReason
            : $"Built from stored tests. Confidence is {ThermalModelFitter.DescribeConfidence(model.Confidence)}. Does not change fans.";
        ModelResults = BuildModelResults(model);
        NotifyModel();
        RefreshOptimizeAvailability();
    }

    public void ShowDiminishingReturns(
        DiminishingReturnsReport report,
        IReadOnlyList<FanSpeedCurve>? curves = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        _returns = report;
        _speedCurves = curves ?? [];
        RpmPlotFans = MeasuredRpmPlot.FanChoices(_speedCurves);
        if (_selectedRpmPlotFanId is null
            || RpmPlotFans.All(fan => !string.Equals(fan.FanGroupId, _selectedRpmPlotFanId, StringComparison.Ordinal)))
        {
            _selectedRpmPlotFanId = RpmPlotFans.FirstOrDefault()?.FanGroupId;
        }

        RebuildRpmPlot();
        DiminishingReturnsProgressText = report.HasRecommendation
            ? "Built from the measured speed curve. Does not change fans."
            : "Need a measured speed-versus-temperature curve (several fan speeds, not just one test step).";
        DiminishingReturnsResults = BuildDiminishingReturnsResults(report);
        NotifyDiminishingReturns();
        RefreshOptimizeAvailability();
    }

    public void UpdateLiveReadings(HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        BindLiveReadings(snapshot);
        OnPropertyChanged(nameof(Temperatures));
        OnPropertyChanged(nameof(PowerReadings));
        OnPropertyChanged(nameof(FanGroups));
    }

    public void ShowExplanation(ExplanationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ExplanationProgressText = "A writeup of stored tests. It does not change fans.";
        ExplanationResults = BuildExplanationResults(report);
        NotifyExplanation();
    }

    public void RememberConfirmation(PolicyConfirmation? confirmation, DriftAssessment? drift)
    {
        _confirmation = confirmation;
        _drift = drift;
        UpdateRetestAvailability();
        RefreshStoredExplanation();
    }

    public void IgnoreDriftOffer()
    {
        if (_drift is null)
        {
            return;
        }

        _drift = _drift with { Action = DriftAction.None };
        UpdateRetestAvailability();
        RefreshStoredExplanation();
    }

    public void BeginOptimize()
    {
        CanRunBaseline = false;
        CanRunFanTests = false;
        CanRunInteractions = false;
        CanBuildModel = false;
        IsOptimizeRunning = true;
        IsHoldingPolicy = false;
        SelectTab(ShellTab.Home);
        OptimizeButtonText = "Cancel";
        OptimizeProgressText = "Applying the chosen fan speeds.";
        CanOptimize = true;
        CanEditPriorities = false;
        OptimizeUnavailableReason = string.Empty;
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        NotifyOptimize();
        NotifyShell();
    }

    public void BeginHold()
    {
        CanRunBaseline = false;
        CanRunFanTests = false;
        CanRunInteractions = false;
        CanBuildModel = false;
        IsOptimizeRunning = true;
        IsHoldingPolicy = true;
        SelectTab(ShellTab.Home);
        OptimizeButtonText = "Stop";
        CanOptimize = true;
        CanEditPriorities = false;
        OptimizeUnavailableReason = string.Empty;
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        NotifyOptimize();
        NotifyShell();
    }

    public void SelectTab(ShellTab tab)
    {
        if (SelectedTab == tab)
        {
            return;
        }

        SelectedTab = tab;
        NotifyShell();
    }

    public void ReportOptimize(PolicyProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        OptimizeProgressText = progress.Message;
        OnPropertyChanged(nameof(OptimizeProgressText));
    }

    public void FinishOptimize()
    {
        IsOptimizeRunning = false;
        IsHoldingPolicy = false;
        SelectTab(ShellTab.Home);
        CanRunBaseline = !IsFanTestRunning && !IsInteractionRunning;
        CanRunFanTests = !IsBaselineRunning && !IsInteractionRunning;
        CanRunInteractions = !IsBaselineRunning && !IsFanTestRunning;
        CanBuildModel = !IsBaselineRunning && !IsFanTestRunning && !IsInteractionRunning;
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
        OptimizeProgressText = "Stopped. Fans are back on BIOS and the NVIDIA driver.";
        OnPropertyChanged(nameof(OptimizeProgressText));
        NotifyShell();
    }

    public void FailOptimize(string detail)
    {
        IsOptimizeRunning = false;
        IsHoldingPolicy = false;
        SelectTab(ShellTab.Home);
        CanRunBaseline = !IsFanTestRunning && !IsInteractionRunning;
        CanRunFanTests = !IsBaselineRunning && !IsInteractionRunning;
        CanRunInteractions = !IsBaselineRunning && !IsFanTestRunning;
        CanBuildModel = !IsBaselineRunning && !IsFanTestRunning && !IsInteractionRunning;
        NotifyBaseline();
        NotifyFanTest();
        NotifyInteractions();
        NotifyModel();
        RefreshOptimizeAvailability();
        OptimizeProgressText = string.IsNullOrWhiteSpace(detail)
            ? "Stopped. Fans are back on BIOS and the NVIDIA driver."
            : detail;
        OnPropertyChanged(nameof(OptimizeProgressText));
        NotifyShell();
    }

    private void NotifyBaseline()
    {
        OnPropertyChanged(nameof(CanRunBaseline));
        OnPropertyChanged(nameof(IsBaselineRunning));
        OnPropertyChanged(nameof(BaselineProgressText));
        OnPropertyChanged(nameof(BaselineResults));
        OnPropertyChanged(nameof(HasBaselineResults));
    }

    private void NotifyInteractions()
    {
        OnPropertyChanged(nameof(CanRunInteractions));
        OnPropertyChanged(nameof(IsInteractionRunning));
        OnPropertyChanged(nameof(InteractionProgressText));
        OnPropertyChanged(nameof(InteractionResults));
        OnPropertyChanged(nameof(HasInteractionResults));
    }

    private void NotifyFanTest()
    {
        OnPropertyChanged(nameof(CanRunFanTests));
        OnPropertyChanged(nameof(IsFanTestRunning));
        OnPropertyChanged(nameof(FanTestProgressText));
        OnPropertyChanged(nameof(FanTestResults));
        OnPropertyChanged(nameof(HasFanTestResults));
    }

    private void NotifyModel()
    {
        OnPropertyChanged(nameof(CanBuildModel));
        OnPropertyChanged(nameof(ModelProgressText));
        OnPropertyChanged(nameof(ModelResults));
        OnPropertyChanged(nameof(HasModelResults));
    }

    private void NotifyDiminishingReturns()
    {
        OnPropertyChanged(nameof(DiminishingReturnsProgressText));
        OnPropertyChanged(nameof(DiminishingReturnsResults));
        OnPropertyChanged(nameof(HasDiminishingReturnsResults));
        OnPropertyChanged(nameof(RpmPlotFans));
        OnPropertyChanged(nameof(HasRpmPlotFans));
        OnPropertyChanged(nameof(SelectedRpmPlotFan));
        OnPropertyChanged(nameof(SelectedRpmPlotTarget));
        OnPropertyChanged(nameof(IsCpuRpmPlotTarget));
        OnPropertyChanged(nameof(IsGpuRpmPlotTarget));
        OnPropertyChanged(nameof(RpmPlot));
        OnPropertyChanged(nameof(HasRpmPlot));
    }

    private void RebuildRpmPlot()
    {
        RpmPlot = MeasuredRpmPlot.For(
            _speedCurves,
            _returns,
            _selectedRpmPlotFanId,
            _selectedRpmPlotTarget);
    }

    private void NotifyExplanation()
    {
        OnPropertyChanged(nameof(ExplanationProgressText));
        OnPropertyChanged(nameof(ExplanationResults));
        OnPropertyChanged(nameof(HasExplanationResults));
    }

    private void RefreshOptimizeAvailability()
    {
        if (IsOptimizeRunning)
        {
            CanOptimize = true;
            CanEditPriorities = false;
            OptimizeButtonText = IsHoldingPolicy ? "Stop" : "Cancel";
            OptimizeUnavailableReason = string.Empty;
            NotifyOptimize();
            UpdateRetestAvailability();
            PublishEnvironmentChecks();
            return;
        }

        CanEditPriorities = !IsSetupBlocked;
        OptimizeButtonText = "Optimize";
        if (IsSetupBlocked)
        {
            CanOptimize = false;
            OptimizeUnavailableReason = EnvironmentChecks.FirstOrDefault(static row => !row.Passed)?.Detail
                ?? SetupHeadline;
            OptimizeProgressText = SetupHeadline;
            NotifyOptimize();
            UpdateRetestAvailability();
            PublishEnvironmentChecks();
            return;
        }

        bool busy = IsBaselineRunning || IsFanTestRunning || IsInteractionRunning || IsFanPresenceRunning;
        CoolingPolicy? policy = _model is null
            ? null
            : PolicyOptimizer.Recommend(_model, _returns, CurrentPreferences());
        if (busy)
        {
            CanOptimize = false;
            OptimizeUnavailableReason = "Wait until the current test finishes.";
            OptimizeProgressText = OptimizeUnavailableReason;
        }
        else
        {
            CanOptimize = true;
            OptimizeUnavailableReason = string.Empty;
            OptimizeProgressText = policy is null
                ? "Ready. Optimize will observe, test fans, then apply a setting."
                : $"Ready. {policy.Summary}";
        }

        NotifyOptimize();
        UpdateRetestAvailability();
        PublishEnvironmentChecks();
        RefreshStoredExplanation();
    }

    private void RefreshStoredExplanation()
    {
        if (_model is null)
        {
            return;
        }

        CoolingPolicy? policy = PolicyOptimizer.Recommend(_model, _returns, CurrentPreferences());
        ShowExplanation(ExplanationReportBuilder.Build(_model, _returns, policy, _confirmation, _drift));
    }

    private void UpdateRetestAvailability()
    {
        bool busy = IsBaselineRunning || IsFanTestRunning || IsInteractionRunning || IsFanPresenceRunning;
        bool offer = _drift?.Action == DriftAction.OfferRetest && !busy;
        CanRetest = offer;
        CanIgnoreDrift = offer;
        OnPropertyChanged(nameof(CanRetest));
        OnPropertyChanged(nameof(CanIgnoreDrift));
        OnPropertyChanged(nameof(RetestOfferVisible));
    }

    private void NotifyOptimize()
    {
        OnPropertyChanged(nameof(CanOptimize));
        OnPropertyChanged(nameof(OptimizeUnavailableReason));
        OnPropertyChanged(nameof(OptimizeButtonText));
        OnPropertyChanged(nameof(OptimizeProgressText));
        OnPropertyChanged(nameof(CanEditPriorities));
        OnPropertyChanged(nameof(IsOptimizeRunning));
        OnPropertyChanged(nameof(IsHoldingPolicy));
        OnPropertyChanged(nameof(IsCharacterizing));
        OnPropertyChanged(nameof(IsHomeReady));
        OnPropertyChanged(nameof(CanChooseGpu));
    }

    private void NotifyShell()
    {
        OnPropertyChanged(nameof(SelectedTab));
        OnPropertyChanged(nameof(IsHomeTab));
        OnPropertyChanged(nameof(IsLearnedTab));
        OnPropertyChanged(nameof(IsAdvancedTab));
        OnPropertyChanged(nameof(IsHoldingPolicy));
        OnPropertyChanged(nameof(IsCharacterizing));
        OnPropertyChanged(nameof(IsHomeReady));
        OnPropertyChanged(nameof(IsSetupBlocked));
        OnPropertyChanged(nameof(ShowHomeFans));
        OnPropertyChanged(nameof(CanChooseGpu));
    }

    private void PublishFanGroups()
    {
        FanGroups = _hideDisconnectedFans
            ? _allFanGroups.Where(static fan => !fan.LooksUnused).ToArray()
            : _allFanGroups;
        OnPropertyChanged(nameof(FanGroups));
        OnPropertyChanged(nameof(HideDisconnectedFans));
        OnPropertyChanged(nameof(CanHideUnusedFans));
        OnPropertyChanged(nameof(UnusedFansButtonText));
    }

    private bool IsSelectedGpu(GpuDevice gpu)
    {
        if (!string.IsNullOrWhiteSpace(_preferredGpuId)
            && string.Equals(gpu.Id, _preferredGpuId, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(gpu.Name, GpuName, StringComparison.OrdinalIgnoreCase);
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string StatusLabel(EnvironmentCheck check)
    {
        if (check.Passed)
        {
            return "OK";
        }

        return check.Title == EnvironmentStatus.CompetingSoftwareTitle ? "Conflict" : "Missing";
    }

    private void PublishEnvironmentChecks()
    {
        bool baseReady = _baseChecks.All(static row => row.Passed);
        bool busy = IsOptimizeRunning
            || IsBaselineRunning
            || IsFanTestRunning
            || IsInteractionRunning
            || IsFanPresenceRunning;
        CanRunFanPresence = baseReady && !busy;
        CheckRow presence = new(
            FanPresenceRunner.Title,
            PresenceStatus(),
            _presenceDetail,
            _presence.Completed && !IsFanPresenceRunning,
            CanRunFanPresence);
        EnvironmentChecks = [.. _baseChecks, presence];
        IsSetupBlocked = EnvironmentChecks.Any(static row => !row.Passed);
        OnPropertyChanged(nameof(EnvironmentChecks));
        OnPropertyChanged(nameof(IsSetupBlocked));
        OnPropertyChanged(nameof(CanRunFanPresence));
        OnPropertyChanged(nameof(IsFanPresenceRunning));
    }

    private string PresenceStatus()
    {
        if (IsFanPresenceRunning)
        {
            return "Checking";
        }

        if (_presence.Completed)
        {
            return "OK";
        }

        return _presenceDetail.StartsWith("Could not", StringComparison.Ordinal)
            || _presenceDetail.StartsWith("Cancelled", StringComparison.Ordinal)
            || _presenceDetail.StartsWith("Stopped", StringComparison.Ordinal)
            ? "Failed"
            : "Needed";
    }

    private void BindLiveReadings(HardwareSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        Temperatures = snapshot.Sensors
            .Where(static sensor => IsTemperature(sensor.Kind))
            .Select(static sensor => new SensorRow(sensor.Name, FormatValue(sensor)))
            .ToArray();
        PowerReadings = snapshot.Sensors
            .Where(static sensor => IsPower(sensor.Kind))
            .Select(static sensor => new SensorRow(sensor.Name, FormatValue(sensor)))
            .ToArray();
        _allFanGroups = snapshot.FanGroups
            .Select(fan => new FanRow(
                fan.Id,
                fan.Name,
                fan.DutyCyclePercent is int duty ? $"{duty}%" : "unknown",
                fan.Rpm is double rpm ? $"{rpm:0} RPM" : "unknown",
                string.IsNullOrWhiteSpace(fan.ControllerName) ? "unknown" : fan.ControllerName,
                ControlNoteOf(fan),
                fan.Kind == FanGroupKind.Pump ? "Pump" : "Fan",
                fan.Rpm is > 0,
                FanWriteCandidates.IsGpuHeader(fan),
                _presence.IsEmpty(fan.Id),
                _presence.Completed))
            .ToArray();
        PublishFanGroups();
        CpuTemperatureText = FormatPreferred(
            PreferredTemperature.Read(snapshot, SensorKind.CpuTemperature),
            "°C");
        GpuTemperatureText = FormatPreferred(
            PreferredTemperature.Read(snapshot, SensorKind.GpuTemperature),
            "°C");
        CpuPowerText = FormatPreferred(PreferredPower.Read(snapshot, SensorKind.CpuPower), "W");
        GpuPowerText = FormatPreferred(PreferredPower.Read(snapshot, SensorKind.GpuPower), "W");
        OnPropertyChanged(nameof(CpuTemperatureText));
        OnPropertyChanged(nameof(GpuTemperatureText));
        OnPropertyChanged(nameof(CpuPowerText));
        OnPropertyChanged(nameof(GpuPowerText));
    }

    private static string FormatPreferred(double? value, string unit) =>
        value is double reading ? $"{reading:0.#} {unit}" : "unknown";

    private static string ControlNoteOf(FanGroup fan)
    {
        if (fan.Kind == FanGroupKind.Pump)
        {
            return "Pump (not written)";
        }

        if (FanWriteCandidates.IsGpuHeader(fan) && !FanWriteCandidates.IsWritableNvidiaGpuFan(fan))
        {
            return "GPU (not written)";
        }

        return fan.IsControllable ? "Controllable" : "Read-only";
    }

    private static bool IsTemperature(SensorKind kind) =>
        kind is SensorKind.CpuTemperature
            or SensorKind.GpuTemperature
            or SensorKind.VrmTemperature
            or SensorKind.MotherboardTemperature
            or SensorKind.CaseTemperature
            or SensorKind.AmbientTemperature
            or SensorKind.OtherTemperature;

    private static bool IsPower(SensorKind kind) =>
        kind is SensorKind.CpuPower or SensorKind.GpuPower;

    private static string FormatValue(SensorReading sensor)
    {
        if (sensor.Value is not double value)
        {
            return "unknown";
        }

        return $"{value:0.#} {sensor.Unit}";
    }

    private static IReadOnlyList<SensorRow> BuildResults(BaselineRun run)
    {
        var rows = new List<SensorRow>
        {
            MetricRow(run, BaselineMetricNames.AmbientCelsius, "Ambient"),
            MetricRow(run, BaselineMetricNames.CpuEverydayRiseCelsius, "CPU everyday rise"),
            MetricRow(run, BaselineMetricNames.GpuEverydayRiseCelsius, "GPU everyday rise"),
            MetricRow(run, BaselineMetricNames.CpuRiseCelsius, "CPU rise"),
            MetricRow(run, BaselineMetricNames.GpuRiseCelsius, "GPU rise"),
            MetricRow(run, BaselineMetricNames.CpuDecayCelsius, "CPU decay"),
            MetricRow(run, BaselineMetricNames.GpuDecayCelsius, "GPU decay"),
            MetricRow(run, BaselineMetricNames.CpuSettleSeconds, "CPU settle"),
            MetricRow(run, BaselineMetricNames.GpuSettleSeconds, "GPU settle"),
            new SensorRow("GPU load available", run.GpuLoadAvailable ? "yes" : "no — CPU-only heat"),
        };
        if (ReferenceStability.FromBaseline(run) is ReferenceAssessment assessment)
        {
            rows.Add(new SensorRow("GPU steadiness", FormatSteadiness(assessment.Gpu)));
            rows.Add(new SensorRow("CPU steadiness", FormatSteadiness(assessment.Cpu)));
        }

        HardwareSnapshot? last = run.Samples.Count == 0 ? null : run.Samples[^1].Snapshot;
        if (last is not null)
        {
            rows.Add(new SensorRow("CPU clock (last)", FirstValue(last, SensorKind.CpuClock, "MHz")));
            rows.Add(new SensorRow("GPU clock (last)", FirstValue(last, SensorKind.GpuClock, "MHz")));
            rows.Add(new SensorRow("CPU load (last)", FirstValue(last, SensorKind.CpuLoad, "%")));
            rows.Add(new SensorRow("GPU load (last)", FirstValue(last, SensorKind.GpuLoad, "%")));
        }

        return rows;
    }

    private static IReadOnlyList<SensorRow> BuildModelResults(ThermalModel model)
    {
        var rows = new List<SensorRow>
        {
            new(
                "How to read this",
                "Measured = a temperature change we recorded. Modeled = a prediction from those tests for the same extra fan speed (about 25% more duty). Unknown = not enough data. This is not a case recommendation."),
            new(
                "This model is for",
                $"{model.CpuName} / {model.GpuName} / {model.MotherboardName}"),
            new(
                "Confidence",
                $"{ThermalModelFitter.DescribeConfidence(model.Confidence)} — {model.ConfidenceReason}"),
        };

        if (model.Confidence == ModelConfidence.None)
        {
            return rows;
        }

        foreach (InfluenceEntry entry in model.Influence)
        {
            if (entry.Evidence != MetricEvidence.Measured || entry.DeltaCelsius is null)
            {
                continue;
            }

            rows.Add(new SensorRow(
                ThermalModelFitter.DescribeRelationship(entry),
                "Measured"));
        }

        foreach (string groupId in model.MeasuredGroupIds())
        {
            string name = model.GroupName(groupId);
            rows.Add(PredictionRow(model, [groupId], $"{name} on the CPU", InfluenceTarget.Cpu));
            rows.Add(PredictionRow(model, [groupId], $"{name} on the GPU", InfluenceTarget.Gpu));
        }

        foreach ((string firstId, string secondId) in model.MeasuredPairs())
        {
            string pair = $"{model.GroupName(firstId)} and {model.GroupName(secondId)}";
            rows.Add(PredictionRow(model, [firstId, secondId], $"{pair} on the CPU", InfluenceTarget.Cpu));
            rows.Add(PredictionRow(model, [firstId, secondId], $"{pair} on the GPU", InfluenceTarget.Gpu));
        }

        return rows;
    }

    private static SensorRow PredictionRow(
        ThermalModel model,
        IReadOnlyList<string> groupIds,
        string label,
        InfluenceTarget target)
    {
        ThermalPrediction prediction = model.Predict(groupIds, target);
        if (prediction.DeltaCelsius is not double delta)
        {
            return new SensorRow(label, "unknown");
        }

        string direction = delta >= 0 ? "cooler" : "warmer";
        string fans = groupIds.Count == 1 ? "that fan" : "those fans";
        return new SensorRow(
            label,
            $"about {Math.Abs(delta):0.0} °C {direction} if we speed {fans} the same way as the test · Modeled");
    }

    private static IReadOnlyList<SensorRow> BuildDiminishingReturnsResults(DiminishingReturnsReport report)
    {
        var rows = new List<SensorRow>
        {
            new(
                "How to read this",
                "Measured = a speed-versus-temperature curve we recorded. Unknown = not enough speeds to find a plateau. This does not change fans."),
        };

        foreach (DiminishingReturnsBand band in report.Bands)
        {
            string target = InteractionBuilder.TargetName(band.Target);
            string name = $"{band.FanGroupName} on the {target}";
            rows.Add(new SensorRow($"{name}, useful", FormatBandRange(band.UsefulRpmMin, band.UsefulRpmMax, band.Evidence)));
            rows.Add(new SensorRow($"{name}, wasted", FormatWasted(band)));
            rows.Add(new SensorRow($"{name}, recommended", FormatRecommended(band)));
        }

        return rows;
    }

    private static IReadOnlyList<SensorRow> BuildExplanationResults(ExplanationReport report)
    {
        var rows = new List<SensorRow>
        {
            new(
                "How to read this",
                "Measured = a temperature change or speed we recorded. Modeled = a prediction or the recommended setting. Inferred = a guess about airflow. Unknown = not enough data."),
        };
        foreach (ExplanationSection section in report.Sections)
        {
            foreach (ExplanationClaim claim in section.Claims)
            {
                rows.Add(new SensorRow(
                    $"{section.Heading}: {claim.Title}",
                    $"{claim.Text} · {claim.Evidence}"));
            }
        }

        return rows;
    }

    private static string FormatBandRange(double? minRpm, double? maxRpm, MetricEvidence evidence)
    {
        if (evidence == MetricEvidence.Unknown || minRpm is not double min || maxRpm is not double max)
        {
            return "unknown";
        }

        return $"{DiminishingReturnsAnalyzer.DescribeRange(min, max)} · Measured";
    }

    private static string FormatWasted(DiminishingReturnsBand band)
    {
        if (band.Evidence == MetricEvidence.Unknown)
        {
            return "unknown";
        }

        if (band.WastedRpmMin is not double min || band.WastedRpmMax is not double max)
        {
            return "none · Measured";
        }

        return $"{DiminishingReturnsAnalyzer.DescribeRange(min, max)} · Measured";
    }

    private static string FormatRecommended(DiminishingReturnsBand band)
    {
        if (band.Evidence == MetricEvidence.Unknown || band.RecommendedRpm is not double rpm)
        {
            return "unknown";
        }

        return $"{DiminishingReturnsAnalyzer.DescribeRpm(rpm)} · Measured";
    }

    private static IReadOnlyList<SensorRow> BuildFanTestResults(FanTestRun run)
    {
        var rows = new List<SensorRow>
        {
            new(
                "How to read this",
                "Measured = temperature change we recorded. Unknown = no sensor. Skipped = we did not change that fan."),
        };
        foreach (InfluenceEntry entry in run.Influence)
        {
            rows.Add(new SensorRow(
                $"{entry.FanGroupName} on the {TargetLabel(entry.Target)}",
                FormatInfluence(entry)));
        }

        foreach (SkippedFanGroup skip in run.Skipped)
        {
            rows.Add(new SensorRow(skip.FanGroupName, $"Not tested — {skip.Reason}"));
        }

        return rows;
    }

    private static IReadOnlyList<SensorRow> BuildInteractionResults(InteractionRun run)
    {
        var rows = new List<SensorRow>
        {
            new(
                "How to read this",
                "Measured = temperature change we recorded. Inferred = a guess. Unknown = no sensor. Skipped = we did not change that fan."),
        };
        foreach (InteractionEntry entry in run.Effects)
        {
            string pair = $"{entry.FirstGroupName} and {entry.SecondGroupName}";
            string target = InteractionBuilder.TargetName(entry.Target);
            string onTarget = $"{pair}: on the {target}";
            if (entry.Evidence == MetricEvidence.Unknown)
            {
                rows.Add(new SensorRow($"{onTarget}, no sensor", "unknown"));
                continue;
            }

            rows.Add(new SensorRow($"{onTarget}, {entry.FirstGroupName} by itself", Cooler(entry.FirstDeltaCelsius)));
            rows.Add(new SensorRow($"{onTarget}, {entry.SecondGroupName} by itself", Cooler(entry.SecondDeltaCelsius)));
            rows.Add(new SensorRow($"{onTarget}, both together", Cooler(entry.CombinedDeltaCelsius)));
            rows.Add(new SensorRow($"{onTarget}, leftover vs adding them", Cooler(entry.ResidualCelsius)));
            if (!string.IsNullOrWhiteSpace(entry.InferredNote))
            {
                rows.Add(new SensorRow($"{onTarget}, possible meaning", $"{entry.InferredNote} · Inferred"));
            }
        }

        foreach (SkippedFanGroup skip in run.Skipped)
        {
            rows.Add(new SensorRow(skip.FanGroupName, $"Not tested — {skip.Reason}"));
        }

        return rows;
    }

    private static string Cooler(double? delta)
    {
        if (delta is not double value)
        {
            return "unknown";
        }

        return $"{Math.Abs(value):0.0} °C {(value >= 0 ? "cooler" : "warmer")} · Measured";
    }

    private static string FormatInfluence(InfluenceEntry entry)
    {
        if (entry.Evidence == MetricEvidence.Unknown || entry.DeltaCelsius is not double delta)
        {
            return string.IsNullOrWhiteSpace(entry.SkipReason) ? "unknown" : $"unknown — {entry.SkipReason}";
        }

        string band = EffectLabel(entry.Effect);
        string direction = delta >= 0 ? "cooler" : "warmer";
        return $"{Math.Abs(delta):0.0} °C {direction} · {band} · Measured";
    }

    private static string EffectLabel(InfluenceEffect? effect) =>
        effect switch
        {
            InfluenceEffect.None => "none",
            InfluenceEffect.Low => "low",
            InfluenceEffect.Medium => "medium",
            InfluenceEffect.High => "high",
            InfluenceEffect.VeryHigh => "very high",
            _ => "unknown",
        };

    private static string TargetLabel(InfluenceTarget target) =>
        target switch
        {
            InfluenceTarget.Cpu => "CPU",
            InfluenceTarget.Gpu => "GPU",
            InfluenceTarget.Vrm => "VRM",
            InfluenceTarget.Case => "Case",
            _ => target.ToString(),
        };

    private static string FormatSteadiness(SensorStabilityResult result)
    {
        string need = $"{result.MinimumDetectableCelsius:0.#} °C";
        return result.State switch
        {
            SensorStability.Stable => $"steady — need {need} to count as a fan effect",
            SensorStability.Drifting =>
                $"wandered {result.RangeCelsius:0.#} °C — cooling stays unproven",
            SensorStability.Noisy =>
                $"jumped {result.RangeCelsius:0.#} °C — cooling stays unproven",
            _ => "missing",
        };
    }

    private static SensorRow MetricRow(BaselineRun run, string name, string label)
    {
        BaselineMetric? metric = run.Metrics.FirstOrDefault(item => item.Name == name);
        if (metric is null || metric.Evidence == MetricEvidence.Unknown || metric.Value is not double value)
        {
            return new SensorRow(label, "unknown");
        }

        return new SensorRow(label, $"{value:0.#} {metric.Unit} · Measured");
    }

    private static string FirstValue(HardwareSnapshot snapshot, SensorKind kind, string unit)
    {
        if (PreferredTemperature.Read(snapshot, kind) is not double value)
        {
            return "unknown";
        }

        return $"{value:0.#} {unit}";
    }

    private static string DescribeStatus(DummySessionResult session)
    {
        return session.Status switch
        {
            DummySessionStatus.Observed when session.Snapshot.IsDemoHardware =>
                "Demo observation stored in memory.",
            DummySessionStatus.Observed => "Observation from this PC stored in memory.",
            DummySessionStatus.DutyApplied => "Demo duty change stored in memory.",
            DummySessionStatus.DutyRejected => session.Detail ?? "Duty change rejected.",
            DummySessionStatus.Aborted => session.Detail ?? "Session aborted by safety limits.",
            _ => session.Status.ToString(),
        };
    }
}

public enum ShellTab
{
    Home,
    Learned,
    Advanced,
}

public sealed record CheckRow(
    string Title,
    string Status,
    string Detail,
    bool Passed,
    bool CanRun = false);

public sealed record SensorRow(string Name, string Value);

public sealed record FanRow(
    string Id,
    string Name,
    string Duty,
    string Rpm,
    string Controller,
    string ControlNote,
    string Kind,
    bool HasTach,
    bool IsGpuHeader,
    bool IsMeasuredEmpty,
    bool PresenceKnown)
{
    public bool LooksUnused =>
        !HasTach && (PresenceKnown ? IsMeasuredEmpty : !IsGpuHeader);
}
