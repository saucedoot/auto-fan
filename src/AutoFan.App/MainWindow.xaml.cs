using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AutoFan.App.ViewModels;
using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;

namespace AutoFan.App;

public partial class MainWindow : Window
{
    private readonly LibreHardwareMonitorBackend _hardware;
    private readonly SyntheticWorkloadActuator _workload;
    private readonly CompetingSoftwareScanner _scanner;
    private readonly SqliteBaselineStore _baselineStore;
    private readonly SqliteFanTestStore _fanTestStore;
    private readonly SqliteInteractionStore _interactionStore;
    private readonly SqlitePolicyConfirmationStore _confirmationStore;
    private readonly JsonUserSettingsStore _settingsStore;
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _policyTimer;
    private readonly DispatcherTimer _liveTimer;
    private CancellationTokenSource? _baselineCts;
    private CancellationTokenSource? _fanTestCts;
    private CancellationTokenSource? _interactionCts;
    private CancellationTokenSource? _optimizeCts;
    private CancellationTokenSource? _retestCts;
    private CancellationTokenSource? _presenceCts;
    private PolicySession? _policySession;
    private ThermalModel? _heldModel;
    private DiminishingReturnsReport? _heldReturns;
    private CoolingPolicy? _heldPolicy;
    private DriftAssessment? _ignoredDrift;
    private DriftAssessment? _lastDrift;
    private DateTimeOffset? _lastConfirmedAt;
    private bool _confirming;
    private bool _confirmedSustainedNonDesktop;

    private OptimizeWalkViewModel? _walk;
    private OptimizeWalkWindow? _walkWindow;
    private bool _walkClosing;
    private bool _walkPairsSkipped;

    public MainWindow()
    {
        _hardware = new LibreHardwareMonitorBackend();
        _scanner = new CompetingSoftwareScanner();
        _baselineStore = SqliteBaselineStore.OpenLocalAppData();
        _fanTestStore = SqliteFanTestStore.OpenLocalAppData();
        _interactionStore = SqliteInteractionStore.OpenLocalAppData();
        _confirmationStore = SqlitePolicyConfirmationStore.OpenLocalAppData();
        _settingsStore = JsonUserSettingsStore.OpenLocalAppData();
        CoolingPreferences preferences = _settingsStore.Load();
        _hardware.SetPreferredGpu(preferences.PreferredGpuId);
        Closed += OnClosed;
        Dispatcher.UnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;

        var store = new InMemorySessionStore();
        var session = new DummySession(_hardware, store);
        DummySessionResult result = session.Run();
        _workload = new SyntheticWorkloadActuator(_hardware.Identity.GpuName);
        EnvironmentStatus environment = EnvironmentProbe.Check(_hardware.OpenError);
        _viewModel = new MainViewModel(result, _hardware.Identity, environment, preferences);
        _viewModel.SetAvailableGpus(_hardware.Gpus);
        DataContext = _viewModel;
        InitializeComponent();
        _policyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _policyTimer.Tick += OnPolicyTick;
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _liveTimer.Tick += OnLiveTick;
        HydrateStoredRuns();
        RefreshModel();
        _viewModel.UpdateLiveReadings(_hardware.ReadSnapshot());
        _liveTimer.Start();
    }

    private void OnLiveTick(object? sender, EventArgs e)
    {
        _viewModel.UpdateLiveReadings(_hardware.ReadSnapshot());
    }

    private void HydrateStoredRuns()
    {
        if (_baselineStore.GetLatest() is BaselineRun baseline)
        {
            _viewModel.FinishBaseline(baseline);
        }

        if (_fanTestStore.GetLatest() is FanTestRun fanTest)
        {
            _viewModel.FinishFanTest(fanTest);
        }

        if (_interactionStore.GetLatest() is InteractionRun interaction)
        {
            _viewModel.FinishInteraction(interaction);
        }
    }

    private ThermalAbortLimits CurrentAbortLimits() =>
        _viewModel.CurrentPreferences().AbortLimits;

    private FanPresence? CurrentPresence()
    {
        FanPresence presence = _viewModel.CurrentPreferences().ConnectedFans;
        return presence.Completed ? presence : null;
    }

    private HeatProfile? StoredLowHeat() => _baselineStore.GetLatest()?.LowProfile;

    private HeatProfile? StoredEverydayHeat() => _baselineStore.GetLatest()?.EverydayProfile;

    private async void OnRunBaseline(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBaselineRunning
            || _viewModel.IsFanTestRunning
            || _viewModel.IsInteractionRunning
            || _viewModel.IsOptimizeRunning
            || _viewModel.IsFanPresenceRunning)
        {
            return;
        }

        _baselineCts?.Dispose();
        _baselineCts = new CancellationTokenSource();
        _viewModel.BeginBaseline();
        var progress = new Progress<BaselineProgress>(update => _viewModel.ReportBaseline(update));
        var runner = new BaselineRunner(_hardware, _workload, _baselineStore, limits: CurrentAbortLimits());

        try
        {
            BaselineRun run = await runner.RunAsync(_baselineCts.Token, progress).ConfigureAwait(true);
            _viewModel.FinishBaseline(run);
            RefreshModel();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _workload.Stop();
            _viewModel.FailBaseline(exception.Message);
        }
    }

    private void OnCancelBaseline(object sender, RoutedEventArgs e)
    {
        _baselineCts?.Cancel();
    }

    private async void OnRunFanTests(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsFanTestRunning
            || _viewModel.IsBaselineRunning
            || _viewModel.IsInteractionRunning
            || _viewModel.IsOptimizeRunning
            || _viewModel.IsFanPresenceRunning)
        {
            return;
        }

        _fanTestCts?.Dispose();
        _fanTestCts = new CancellationTokenSource();
        _viewModel.BeginFanTest();
        var progress = new Progress<FanTestProgress>(update => _viewModel.ReportFanTest(update));
        var runner = new FanTestRunner(
            _hardware,
            _workload,
            _scanner,
            _fanTestStore,
            limits: CurrentAbortLimits(),
            presence: CurrentPresence(),
            gpuHeatUseful: GpuHeat.IsUseful(_baselineStore.GetLatest()),
            stability: ReferenceStability.FromBaseline(_baselineStore.GetLatest()),
            lowHeat: StoredLowHeat(),
            everydayHeat: StoredEverydayHeat(),
            everydayGpuHeatUseful: GpuHeat.EverydayIsUseful(_baselineStore.GetLatest()),
            baseline: _baselineStore.GetLatest());

        try
        {
            FanTestRun run = await runner.RunAsync(_fanTestCts.Token, progress).ConfigureAwait(true);
            _viewModel.FinishFanTest(run);
            RefreshModel();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
            _viewModel.FailFanTest(exception.Message);
        }
    }

    private void OnCancelFanTests(object sender, RoutedEventArgs e)
    {
        _fanTestCts?.Cancel();
    }

    private async void OnRunInteractions(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsInteractionRunning
            || _viewModel.IsBaselineRunning
            || _viewModel.IsFanTestRunning
            || _viewModel.IsOptimizeRunning
            || _viewModel.IsFanPresenceRunning)
        {
            return;
        }

        _interactionCts?.Dispose();
        _interactionCts = new CancellationTokenSource();
        _viewModel.BeginInteraction();
        var progress = new Progress<InteractionProgress>(update => _viewModel.ReportInteraction(update));
        var runner = new InteractionRunner(
            _hardware,
            _workload,
            _scanner,
            _interactionStore,
            limits: CurrentAbortLimits(),
            presence: CurrentPresence(),
            lowHeat: StoredLowHeat());

        try
        {
            InteractionRun run = await runner.RunAsync(
                _interactionCts.Token,
                progress,
                _fanTestStore.GetLatest()?.Influence).ConfigureAwait(true);
            _viewModel.FinishInteraction(run);
            _walkPairsSkipped = false;
            RefreshModel();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
            _viewModel.FailInteraction(exception.Message);
        }
    }

    private void OnCancelInteractions(object sender, RoutedEventArgs e)
    {
        _interactionCts?.Cancel();
    }

    private void OnBuildModel(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBaselineRunning
            || _viewModel.IsFanTestRunning
            || _viewModel.IsInteractionRunning
            || _viewModel.IsOptimizeRunning
            || _viewModel.IsFanPresenceRunning)
        {
            return;
        }

        RefreshModel();
    }

    private void OnPickGpu(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanChooseGpu || sender is not Button button)
        {
            return;
        }

        var menu = new ContextMenu();
        foreach (GpuChoice gpu in _viewModel.Gpus)
        {
            var item = new MenuItem
            {
                Header = gpu.Name,
                IsChecked = gpu.IsSelected,
                Tag = gpu.Id,
            };
            item.Click += OnGpuChosen;
            menu.Items.Add(item);
        }

        button.ContextMenu = menu;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void OnGpuChosen(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string id || !_viewModel.ChooseGpu(id))
        {
            return;
        }

        _hardware.SetPreferredGpu(id);
        HardwareSnapshot snapshot = _hardware.ReadSnapshot();
        _viewModel.ApplyIdentity(_hardware.Identity);
        _viewModel.SetAvailableGpus(_hardware.Gpus);
        _viewModel.UpdateLiveReadings(snapshot);
        _workload.PreferGpu(_viewModel.GpuName);
        _settingsStore.Save(_viewModel.CurrentPreferences());
    }

    private async void OnFindConnectedFans(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanRunFanPresence
            || _viewModel.IsFanPresenceRunning
            || _viewModel.IsOptimizeRunning
            || _viewModel.IsBaselineRunning
            || _viewModel.IsFanTestRunning
            || _viewModel.IsInteractionRunning)
        {
            return;
        }

        _presenceCts?.Dispose();
        _presenceCts = new CancellationTokenSource();
        _workload.Stop();
        _viewModel.BeginFanPresence();
        var progress = new Progress<FanPresenceProgress>(update => _viewModel.ReportFanPresence(update));
        var runner = new FanPresenceRunner(
            _hardware,
            _scanner,
            limits: CurrentAbortLimits());

        try
        {
            FanPresenceReport report = await runner
                .RunAsync(_presenceCts.Token, progress)
                .ConfigureAwait(true);
            _viewModel.FinishFanPresence(report);
            _settingsStore.Save(_viewModel.CurrentPreferences());
            _viewModel.UpdateLiveReadings(_hardware.ReadSnapshot());
        }
        catch (OperationCanceledException)
        {
            _hardware.RestoreDefaults();
            _viewModel.FailFanPresence("Cancelled. Fans are back on BIOS and the NVIDIA driver.");
        }
        catch (Exception exception)
        {
            _hardware.RestoreDefaults();
            _viewModel.FailFanPresence(exception.Message);
        }
    }

    private void OnToggleUnusedFans(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleUnusedFans();
        _settingsStore.Save(_viewModel.CurrentPreferences());
    }

    private void OnSelectHome(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectTab(ShellTab.Home);
    }

    private void OnSelectLearned(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectTab(ShellTab.Learned);
    }

    private void OnSelectAdvanced(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectTab(ShellTab.Advanced);
    }

    private void OnOptimize(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsOptimizeRunning)
        {
            StopOptimize(stoppedByUser: true);
            return;
        }

        if (_viewModel.IsBaselineRunning
            || _viewModel.IsFanTestRunning
            || _viewModel.IsInteractionRunning
            || _viewModel.IsFanPresenceRunning
            || _walkWindow is not null)
        {
            return;
        }

        _confirmedSustainedNonDesktop = false;
        _ignoredDrift = null;
        _lastConfirmedAt = null;
        _walkClosing = false;
        _walkPairsSkipped = false;
        _walk = new OptimizeWalkViewModel();
        _walk.PrimaryRequested += OnWalkPrimary;
        _walk.CancelRequested += OnWalkCancel;
        _walkWindow = new OptimizeWalkWindow(_walk) { Owner = this };
        _walkWindow.ShowDialog();
        DetachWalk();
    }

    private async void OnWalkPrimary(object? sender, EventArgs e)
    {
        if (_walk is null)
        {
            return;
        }

        switch (_walk.Step)
        {
            case OptimizeWalkStep.Watch:
                await RunWalkWatchAsync().ConfigureAwait(true);
                break;
            case OptimizeWalkStep.Fans:
                await RunWalkFansAsync().ConfigureAwait(true);
                break;
            case OptimizeWalkStep.Hold:
                await RunWalkHoldAsync().ConfigureAwait(true);
                break;
        }
    }

    private async void OnWalkCancel(object? sender, EventArgs e)
    {
        if (_walkClosing)
        {
            return;
        }

        _walkClosing = true;
        _optimizeCts?.Cancel();
        bool apply = _walk?.TestsStarted == true;
        if (!apply)
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
            if (_viewModel.IsOptimizeRunning)
            {
                _viewModel.FinishOptimize();
            }

            CloseWalk();
            return;
        }

        try
        {
            await ApplyRecommendedPolicyAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
            _viewModel.FailOptimize(exception.Message);
        }

        CloseWalk();
    }

    private async Task RunWalkWatchAsync()
    {
        if (_walk is null)
        {
            return;
        }

        _optimizeCts?.Dispose();
        _optimizeCts = new CancellationTokenSource();
        CancellationToken token = _optimizeCts.Token;
        _walk.BeginCurrent();
        _viewModel.BeginOptimize();
        ReportWalk("Watching this PC. Fans stay on BIOS and the NVIDIA driver.");
        try
        {
            BaselineRun baseline = await new BaselineRunner(
                    _hardware,
                    _workload,
                    _baselineStore,
                    limits: CurrentAbortLimits())
                .RunAsync(
                    token,
                    new Progress<BaselineProgress>(update => ReportWalk(update.Message)))
                .ConfigureAwait(true);
            _viewModel.FinishBaseline(baseline);
            _viewModel.BeginOptimize();
            if (_walkClosing)
            {
                return;
            }

            if (!OptimizeWalkOutcome.ContinueAfterWatch(baseline.Status))
            {
                _walk.Fail(OptimizeWalkOutcome.WatchFailDetail(baseline));
                return;
            }

            _walk.CompleteCurrent(OptimizeWalkOutcome.WatchCompleteDetail(baseline));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
            _viewModel.FailOptimize(exception.Message);
            _walk.Fail(exception.Message);
        }
    }

    private async Task RunWalkFansAsync()
    {
        if (_walk is null)
        {
            return;
        }

        _optimizeCts?.Dispose();
        _optimizeCts = new CancellationTokenSource();
        CancellationToken token = _optimizeCts.Token;
        _walk.BeginCurrent();
        _viewModel.BeginOptimize();
        ReportWalk("Testing fans. Waiting until CPU and GPU are cool enough.");
        try
        {
            FanTestRun fanTest = await new FanTestRunner(
                    _hardware,
                    _workload,
                    _scanner,
                    _fanTestStore,
                    limits: CurrentAbortLimits(),
                    presence: CurrentPresence(),
                    gpuHeatUseful: GpuHeat.IsUseful(_baselineStore.GetLatest()),
                    stability: ReferenceStability.FromBaseline(_baselineStore.GetLatest()),
                    lowHeat: StoredLowHeat(),
                    everydayHeat: StoredEverydayHeat(),
                    everydayGpuHeatUseful: GpuHeat.EverydayIsUseful(_baselineStore.GetLatest()),
                    baseline: _baselineStore.GetLatest())
                .RunAsync(
                    token,
                    new Progress<FanTestProgress>(update => ReportWalk(update.Message)))
                .ConfigureAwait(true);
            _viewModel.FinishFanTest(fanTest);
            _viewModel.BeginOptimize();
            if (token.IsCancellationRequested || _walkClosing)
            {
                return;
            }

            WalkFansNext next = OptimizeWalkOutcome.AfterFans(fanTest);
            if (next == WalkFansNext.Retry)
            {
                _walk.Fail(OptimizeWalkOutcome.FansFailDetail(fanTest));
                return;
            }

            if (next == WalkFansNext.RunPairs)
            {
                InteractionRun interaction = await new InteractionRunner(
                        _hardware,
                        _workload,
                        _scanner,
                        _interactionStore,
                        limits: CurrentAbortLimits(),
                        presence: CurrentPresence(),
                        lowHeat: StoredLowHeat())
                    .RunAsync(
                        token,
                        new Progress<InteractionProgress>(update => ReportWalk(update.Message)),
                        _fanTestStore.GetLatest()?.Influence)
                    .ConfigureAwait(true);
                _viewModel.FinishInteraction(interaction);
                _viewModel.BeginOptimize();
                _walkPairsSkipped = false;
            }
            else
            {
                _walkPairsSkipped = true;
            }

            if (_walkClosing)
            {
                return;
            }

            _walk.CompleteCurrent(OptimizeWalkOutcome.FansCompleteDetail(fanTest));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
            _viewModel.FailOptimize(exception.Message);
            _walk.Fail(exception.Message);
        }
    }

    private async Task RunWalkHoldAsync()
    {
        if (_walk is null)
        {
            return;
        }

        _walk.BeginCurrent();
        _viewModel.BeginOptimize();
        ReportWalk("Applying the chosen fan speeds.");
        try
        {
            await ApplyRecommendedPolicyAsync().ConfigureAwait(true);
            if (_walkClosing)
            {
                return;
            }

            CloseWalk();
        }
        catch (OperationCanceledException)
        {
            await ApplyRecommendedPolicyAsync().ConfigureAwait(true);
            CloseWalk();
        }
        catch (Exception exception)
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
            _viewModel.FailOptimize(exception.Message);
            _walk.Fail(exception.Message);
        }
    }

    private void ReportWalk(string message)
    {
        _viewModel.ReportOptimize(new PolicyProgress(message, _hardware.ReadSnapshot()));
        _walk?.ReportStatus(message);
    }

    private void CloseWalk()
    {
        _walkWindow?.AllowClose();
        _walkWindow?.Close();
    }

    private void DetachWalk()
    {
        if (_walk is not null)
        {
            _walk.PrimaryRequested -= OnWalkPrimary;
            _walk.CancelRequested -= OnWalkCancel;
        }

        _walk = null;
        _walkWindow = null;
        _walkClosing = false;
    }

    private InteractionRun? CurrentInteraction() =>
        _walkPairsSkipped ? null : _interactionStore.GetLatest();

    private async Task ApplyRecommendedPolicyAsync()
    {
        ThermalModel model = ThermalModelFitter.Fit(
            _hardware.Identity,
            _baselineStore.GetLatest(),
            _fanTestStore.GetLatest(),
            CurrentInteraction());
        IReadOnlyList<FanSpeedCurve> curves = FanSpeedCurveBuilder.Build(_fanTestStore.GetLatest()?.Samples);
        DiminishingReturnsReport returns = DiminishingReturnsAnalyzer.Analyze(curves);
        CoolingPreferences preferences = _viewModel.CurrentPreferences();
        _settingsStore.Save(preferences);
        CoolingPolicy? policy = PolicyOptimizer.Recommend(model, returns, preferences);
        _viewModel.ShowModel(model);
        _viewModel.ShowDiminishingReturns(returns, curves);
        _viewModel.ShowExplanation(ExplanationReportBuilder.Build(model, returns, policy));
        if (policy is null)
        {
            _viewModel.FailOptimize(PolicyOptimizer.UnavailableReason(model));
            return;
        }

        var session = new PolicySession(_hardware, _scanner, policy, model: model, limits: CurrentAbortLimits());
        DutySetResult started = session.Start();
        if (!started.Accepted)
        {
            session.Dispose();
            _viewModel.FailOptimize(started.Error ?? "Could not apply the chosen fan speeds.");
            return;
        }

        _heldModel = model;
        _heldReturns = returns;
        _heldPolicy = policy;
        _policySession = session;
        _viewModel.BeginHold();
        _viewModel.ReportOptimize(session.Status());
        _confirming = true;
        _policyTimer.Start();

        try
        {
            PolicyConfirmation confirmation = await ConfirmAppliedAsync(model, policy).ConfigureAwait(true);
            if (_policySession is null)
            {
                return;
            }

            RememberConfirmation(confirmation);
        }
        finally
        {
            _confirming = false;
        }
    }

    private Task<PolicyConfirmation> ConfirmAppliedAsync(ThermalModel model, CoolingPolicy policy)
    {
        HardwareSnapshot start = _hardware.ReadSnapshot();
        (double? expectedCpu, double? expectedGpu) = PolicyConfirmer.ExpectedSettled(
            PreferredTemperature.Read(start, SensorKind.CpuTemperature),
            PreferredTemperature.Read(start, SensorKind.GpuTemperature),
            model,
            policy);
        return PolicyConfirmationRunner.ConfirmAsync(
            _hardware,
            _policySession,
            expectedCpu,
            expectedGpu,
            cancellationToken: _optimizeCts?.Token ?? CancellationToken.None);
    }

    private void RememberConfirmation(PolicyConfirmation confirmation)
    {
        _confirmationStore.Save(confirmation);
        _lastConfirmedAt = confirmation.ObservedAt ?? DateTimeOffset.UtcNow;
        DriftAssessment drift = DriftDetector.Assess(
            confirmation,
            _baselineStore.GetLatest()?.AmbientCelsius,
            _fanTestStore.GetLatest()?.Influence,
            _ignoredDrift);
        _lastDrift = drift;
        _viewModel.RememberConfirmation(confirmation, drift);
    }

    private async void OnPolicyTick(object? sender, EventArgs e)
    {
        if (_policySession is null)
        {
            return;
        }

        if (_confirming)
        {
            _policySession.CheckSafety();
        }
        else
        {
            _policySession.Tick();
        }

        if (_policySession.IsAborted)
        {
            string detail = _policySession.AbortDetail
                ?? "Stopped by a safety limit. Fans are back on BIOS and the NVIDIA driver.";
            StopOptimize(stoppedByUser: false, detail);
            return;
        }

        _viewModel.ReportOptimize(_policySession.Status());
        if (_confirming || _heldModel is null || _heldPolicy is null)
        {
            return;
        }

        WorkloadState workload = _policySession.ReadWorkload();
        if (!PolicyConfirmSchedule.IsDue(
            DateTimeOffset.UtcNow,
            _lastConfirmedAt,
            workload.IsPowerLead,
            _confirmedSustainedNonDesktop))
        {
            return;
        }

        await ConfirmWhileHoldingAsync().ConfigureAwait(true);
    }

    private async Task ConfirmWhileHoldingAsync()
    {
        if (_confirming || _heldModel is null || _heldPolicy is null || _policySession is null)
        {
            return;
        }

        _confirming = true;
        try
        {
            bool sustained = _policySession.ReadWorkload().IsPowerLead;
            PolicyConfirmation confirmation = await ConfirmAppliedAsync(_heldModel, _heldPolicy)
                .ConfigureAwait(true);
            if (_policySession is null)
            {
                return;
            }

            RememberConfirmation(confirmation);
            if (sustained)
            {
                _confirmedSustainedNonDesktop = true;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            _confirming = false;
        }
    }

    private async void OnRetest(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanRetest
            || _viewModel.IsBaselineRunning
            || _viewModel.IsFanTestRunning
            || _viewModel.IsInteractionRunning)
        {
            return;
        }

        IReadOnlyList<string> groupIds = _viewModel.RetestGroupIds;
        if (groupIds.Count == 0)
        {
            return;
        }

        _retestCts?.Dispose();
        _retestCts = new CancellationTokenSource();
        CancellationToken token = _retestCts.Token;
        _confirming = false;
        _policyTimer.Stop();
        _policySession?.Stop();
        _policySession?.Dispose();
        _policySession = null;
        _viewModel.BeginFanTest();
        if (!_viewModel.IsOptimizeRunning)
        {
            _viewModel.BeginOptimize();
        }

        _viewModel.ReportOptimize(new PolicyProgress(
            "Re-checking the fans that look wrong. Waiting until CPU and GPU are cool enough.",
            _hardware.ReadSnapshot()));
        try
        {
            FanTestRun? previous = _fanTestStore.GetLatest();
            FanTestRun targeted = await new FanTestRunner(
                _hardware,
                _workload,
                _scanner,
                _fanTestStore,
                limits: CurrentAbortLimits(),
                presence: CurrentPresence(),
                gpuHeatUseful: GpuHeat.IsUseful(_baselineStore.GetLatest()),
                stability: ReferenceStability.FromBaseline(_baselineStore.GetLatest()),
                lowHeat: StoredLowHeat())
                .RunAsync(
                    token,
                    new Progress<FanTestProgress>(update =>
                        _viewModel.ReportOptimize(new PolicyProgress(update.Message, update.Latest))),
                    groupIds)
                .ConfigureAwait(true);
            FanTestRun merged = InfluenceOverlay.Apply(previous, targeted);
            if (merged.Id != targeted.Id)
            {
                _fanTestStore.Save(merged);
            }

            _viewModel.FinishFanTest(merged);
            _ignoredDrift = null;
            await ApplyRecommendedPolicyAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
            _viewModel.FinishOptimize();
        }
        catch (Exception exception)
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
            _viewModel.FailOptimize(exception.Message);
        }
    }

    private void OnIgnoreDrift(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanIgnoreDrift || _lastDrift is null)
        {
            return;
        }

        _ignoredDrift = _lastDrift;
        _viewModel.IgnoreDriftOffer();
    }

    private void StopOptimize(bool stoppedByUser, string? detail = null)
    {
        _optimizeCts?.Cancel();
        _baselineCts?.Cancel();
        _fanTestCts?.Cancel();
        _interactionCts?.Cancel();
        _retestCts?.Cancel();
        _presenceCts?.Cancel();
        _policyTimer.Stop();
        _confirming = false;
        _policySession?.Stop();
        _policySession?.Dispose();
        _policySession = null;
        if (stoppedByUser)
        {
            _viewModel.FinishOptimize();
        }
        else
        {
            _viewModel.FailOptimize(detail ?? "Stopped. Fans are back on BIOS and the NVIDIA driver.");
        }
    }

    private void RefreshModel()
    {
        ThermalModel model = ThermalModelFitter.Fit(
            _hardware.Identity,
            _baselineStore.GetLatest(),
            _fanTestStore.GetLatest(),
            CurrentInteraction());
        IReadOnlyList<FanSpeedCurve> curves = FanSpeedCurveBuilder.Build(_fanTestStore.GetLatest()?.Samples);
        DiminishingReturnsReport returns = DiminishingReturnsAnalyzer.Analyze(curves);
        _viewModel.ShowModel(model);
        _viewModel.ShowDiminishingReturns(returns, curves);
        PolicyConfirmation? confirmation = _confirmationStore.GetLatest();
        DriftAssessment? drift = confirmation is null
            ? null
            : DriftDetector.Assess(
                confirmation,
                _baselineStore.GetLatest()?.AmbientCelsius,
                _fanTestStore.GetLatest()?.Influence,
                _ignoredDrift);
        _lastDrift = drift;
        _viewModel.RememberConfirmation(confirmation, drift);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        Dispatcher.UnhandledException -= OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandled;
        _settingsStore.Save(_viewModel.CurrentPreferences());
        _liveTimer.Stop();
        StopBaselineAndRestore();
        _baselineStore.Dispose();
        _fanTestStore.Dispose();
        _interactionStore.Dispose();
        _confirmationStore.Dispose();
        _workload.Dispose();
        _hardware.Dispose();
    }

    private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StopBaselineAndRestore();
    }

    private void OnDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        StopBaselineAndRestore();
    }

    private void StopBaselineAndRestore()
    {
        _baselineCts?.Cancel();
        _fanTestCts?.Cancel();
        _interactionCts?.Cancel();
        _retestCts?.Cancel();
        _presenceCts?.Cancel();
        _policyTimer.Stop();
        _confirming = false;
        _policySession?.Dispose();
        _policySession = null;
        _workload.Stop();
        _hardware.RestoreDefaults();
    }
}
