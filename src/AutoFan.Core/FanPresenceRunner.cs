namespace AutoFan.Core;

/// <summary>
/// Sets each writable motherboard header to 100%, and NVIDIA GPU fans once
/// per card, then checks whether each has RPM. Restores BIOS and the NVIDIA
/// driver at the end. Does not add synthetic heat.
/// </summary>
public sealed class FanPresenceRunner
{
    public const int ProbeDutyPercent = 100;
    public const string Title = "Connected fans";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan DefaultSamplePeriod = TimeSpan.FromMilliseconds(200);

    private readonly IHardwareBackend _hardware;
    private readonly ICompetingSoftwareScanner _scanner;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ThermalAbortLimits _limits;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _samplePeriod;

    public FanPresenceRunner(
        IHardwareBackend hardware,
        ICompetingSoftwareScanner scanner,
        TimeProvider? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ThermalAbortLimits? limits = null,
        TimeSpan? timeout = null,
        TimeSpan? samplePeriod = null)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _clock = clock ?? TimeProvider.System;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _limits = ThermalAbortLimits.FromUser(limits?.CpuCelsius, limits?.GpuCelsius);
        _timeout = timeout ?? DefaultTimeout;
        _samplePeriod = samplePeriod ?? DefaultSamplePeriod;
    }

    public async Task<FanPresenceReport> RunAsync(
        CancellationToken cancellationToken = default,
        IProgress<FanPresenceProgress>? progress = null)
    {
        var connected = new List<string>();
        var empty = new List<string>();
        var skipped = new List<SkippedFanGroup>();
        var candidates = new List<FanGroup>();

        try
        {
            foreach (FanGroup group in _hardware.FanGroups)
            {
                if (group.Kind == FanGroupKind.Pump)
                {
                    skipped.Add(new SkippedFanGroup(group.Id, group.Name, FanTestReasons.Pump));
                    continue;
                }

                if (FanWriteCandidates.IsGpuHeader(group) && !FanWriteCandidates.IsWritableNvidiaGpuFan(group))
                {
                    skipped.Add(new SkippedFanGroup(group.Id, group.Name, FanTestReasons.Gpu));
                    continue;
                }

                if (!FanWriteCandidates.IsWritableTestFan(group))
                {
                    if (group.Kind == FanGroupKind.Fan && !group.IsControllable)
                    {
                        skipped.Add(new SkippedFanGroup(
                            group.Id,
                            group.Name,
                            FanTestReasons.NotControllable));
                    }

                    continue;
                }

                candidates.Add(group);
            }

            using var session = new SafeFanSession(_hardware, _scanner, _clock, limits: _limits);
            IReadOnlyList<FanGroup> probes = FanWriteCandidates.TakeOnePerCoupledSet(candidates);
            for (int index = 0; index < probes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FanGroup group = FindGroup(probes[index].Id) ?? probes[index];
                IReadOnlyList<FanGroup> members = FanWriteCandidates.MembersOfCoupledSet(group, candidates);
                progress?.Report(new FanPresenceProgress(
                    $"Setting {group.Name} to 100% and watching RPM ({index + 1} of {probes.Count})."));

                DutySetResult write = session.TrySetDuty(group.Id, ProbeDutyPercent);
                if (session.IsAborted)
                {
                    return Finish(
                        FanPresenceStatus.Aborted,
                        connected,
                        empty,
                        skipped,
                        session.AbortDetail ?? write.Error ?? "Stopped by a safety limit.");
                }

                if (!write.Accepted)
                {
                    foreach (FanGroup member in members)
                    {
                        skipped.Add(new SkippedFanGroup(
                            member.Id,
                            member.Name,
                            write.Error ?? FanTestReasons.NotControllable));
                    }

                    continue;
                }

                DateTimeOffset deadline = _clock.GetUtcNow() + _timeout;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    session.CheckLimits();
                    if (session.IsAborted)
                    {
                        return Finish(
                            FanPresenceStatus.Aborted,
                            connected,
                            empty,
                            skipped,
                            session.AbortDetail ?? "Stopped by a safety limit.");
                    }

                    if (members.Any(member => HasFan(FindGroup(member.Id)?.Rpm)))
                    {
                        break;
                    }

                    if (_clock.GetUtcNow() >= deadline)
                    {
                        break;
                    }

                    await _delay(_samplePeriod, cancellationToken).ConfigureAwait(false);
                }

                foreach (FanGroup member in members)
                {
                    if (HasFan(FindGroup(member.Id)?.Rpm))
                    {
                        connected.Add(member.Id);
                    }
                    else
                    {
                        empty.Add(member.Id);
                    }
                }
            }

            progress?.Report(new FanPresenceProgress(DescribeSuccess(connected.Count, empty.Count)));
            return Finish(FanPresenceStatus.Completed, connected, empty, skipped, detail: null);
        }
        catch (OperationCanceledException)
        {
            _hardware.RestoreDefaults();
            return Finish(
                FanPresenceStatus.Cancelled,
                connected,
                empty,
                skipped,
                "Cancelled. Fans are back on BIOS and the NVIDIA driver.");
        }
        catch
        {
            _hardware.RestoreDefaults();
            throw;
        }
    }

    public static bool HasFan(double? rpm) => rpm is > 0;

    public static bool RpmResponded(double? beforeRpm, double? afterRpm) =>
        HasFan(afterRpm);

    private FanGroup? FindGroup(string id) =>
        _hardware.ReadSnapshot().FanGroups.FirstOrDefault(fan =>
            string.Equals(fan.Id, id, StringComparison.Ordinal));

    private static FanPresenceReport Finish(
        FanPresenceStatus status,
        List<string> connected,
        List<string> empty,
        List<SkippedFanGroup> skipped,
        string? detail)
    {
        return new FanPresenceReport(
            status,
            connected.ToArray(),
            empty.ToArray(),
            skipped.ToArray(),
            detail);
    }

    public static string DescribeSuccess(int connectedCount, int emptyCount)
    {
        string connected = connectedCount == 1
            ? "1 connected fan header"
            : $"{connectedCount} connected fan headers";
        if (emptyCount == 0)
        {
            return $"Found {connected}. AMD and Intel GPU fans were not changed.";
        }

        string empty = emptyCount == 1
            ? "1 empty header"
            : $"{emptyCount} empty headers";
        return $"Found {connected}. Hidden {empty}. AMD and Intel GPU fans were not changed.";
    }

    public static string NeededDetail { get; } =
        "Sets each motherboard header, and NVIDIA GPU fans once per card, to 100%. A header is connected if it has RPM. No heat is added. AMD and Intel GPU fans are not changed.";
}
