namespace AutoFan.Core;

/// <summary>
/// Production write coordinator. Every accepted duty is paired with restore
/// on dispose, cancel, thermal abort, duration abort, or competing software.
/// </summary>
public sealed class SafeFanSession : IDisposable
{
    private readonly IHardwareBackend _hardware;
    private readonly ICompetingSoftwareScanner _scanner;
    private readonly TimeProvider _clock;
    private readonly FanSessionKind _kind;
    private readonly ThermalAbortLimits _limits;
    private readonly ThermalTrend _trend = new();
    private readonly object _gate = new();
    private ITimer? _timer;
    private DateTimeOffset? _controlStartedAt;
    private bool _disposed;

    public SafeFanSession(
        IHardwareBackend hardware,
        ICompetingSoftwareScanner scanner,
        TimeProvider? clock = null,
        FanSessionKind kind = FanSessionKind.Experiment,
        ThermalAbortLimits? limits = null)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _clock = clock ?? TimeProvider.System;
        _kind = kind;
        _limits = ThermalAbortLimits.FromUser(limits?.CpuCelsius, limits?.GpuCelsius);
    }

    public bool IsAborted { get; private set; }

    public string? AbortDetail { get; private set; }

    public DutySetResult TrySetDuty(string fanGroupId, int percent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanGroupId);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (IsAborted)
            {
                return new DutySetResult(false, AbortDetail);
            }

            if (TryCompetingSoftwareMessage() is string competing)
            {
                if (_hardware.HasActiveSoftwareControl)
                {
                    Abort(competing);
                }

                return new DutySetResult(false, competing);
            }

            HardwareSnapshot snapshot = _hardware.ReadSnapshot();
            if (EvaluateSafety(snapshot) is string detail)
            {
                Abort(detail);
                return new DutySetResult(false, detail);
            }

            if (!DutyPercent.TryCreate(percent, out _))
            {
                return new DutySetResult(
                    false,
                    $"Duty cycle {percent}% is outside {SafetyLimits.MinDutyPercent}-{SafetyLimits.MaxDutyPercent}%.");
            }

            DutySetResult result = _hardware.TrySetDuty(fanGroupId, percent);
            if (result.Accepted)
            {
                _controlStartedAt ??= _clock.GetUtcNow();
                EnsureTimer();
            }

            return result;
        }
    }

    public void Restore()
    {
        lock (_gate)
        {
            RestoreCore();
        }
    }

    /// <summary>
    /// Re-checks thermal ceilings, duration, and competing software.
    /// Tests call this after advancing a fake clock.
    /// </summary>
    public void CheckLimits()
    {
        lock (_gate)
        {
            CheckLimitsCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            RestoreCore();
            _disposed = true;
        }
    }

    private void EnsureTimer()
    {
        _timer ??= _clock.CreateTimer(
            static state => ((SafeFanSession)state!).CheckLimits(),
            this,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    private void CheckLimitsCore()
    {
        if (_disposed || IsAborted)
        {
            return;
        }

        if (TryCompetingSoftwareMessage() is string competing && _hardware.HasActiveSoftwareControl)
        {
            Abort(competing);
            return;
        }

        if (EvaluateSafety(_hardware.ReadSnapshot()) is string detail)
        {
            Abort(detail);
            return;
        }

        if (_kind == FanSessionKind.Experiment
            && _controlStartedAt is DateTimeOffset start
            && _clock.GetUtcNow() - start >= TimeSpan.FromMinutes(SafetyLimits.MaxExperimentDurationMinutes))
        {
            Abort(
                $"Software fan control exceeded the {SafetyLimits.MaxExperimentDurationMinutes}-minute abort limit.");
        }
    }

    private string? EvaluateSafety(HardwareSnapshot snapshot)
    {
        _trend.Add(_clock.GetUtcNow(), snapshot);
        ThermalAbortReason? abort = SafetyLimits.Evaluate(snapshot, _limits) ?? _trend.Evaluate(_limits);
        return abort is null ? null : SafetyLimits.Describe(abort.Value, _limits);
    }

    private string? TryCompetingSoftwareMessage()
    {
        IReadOnlyList<string> running = _scanner.DetectRunning();
        if (running.Count == 0)
        {
            return null;
        }

        return "Competing fan software is running: "
            + string.Join(", ", running)
            + ". Close it before AUTO Fan changes fan speeds.";
    }

    private void Abort(string detail)
    {
        IsAborted = true;
        AbortDetail = detail;
        RestoreCore();
    }

    private void RestoreCore()
    {
        StopTimer();
        _controlStartedAt = null;
        _hardware.RestoreDefaults();
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
