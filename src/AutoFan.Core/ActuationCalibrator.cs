namespace AutoFan.Core;

/// <summary>
/// Learns duty-to-RPM for one fan group. It does not wait for temperatures
/// and it does not command 0%. The caller restores fans afterward.
/// </summary>
public sealed class ActuationCalibrator
{
    public static readonly int[] SweepDuties = [100, 70, 50, 40, 30, 20, 15];

    private readonly IHardwareBackend _hardware;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _stepTimeout;
    private readonly TimeSpan _samplePeriod;

    public ActuationCalibrator(
        IHardwareBackend hardware,
        TimeProvider clock,
        Func<TimeSpan, CancellationToken, Task> delay,
        TimeSpan? stepTimeout = null,
        TimeSpan? samplePeriod = null)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _stepTimeout = stepTimeout ?? TimeSpan.FromSeconds(2);
        _samplePeriod = samplePeriod ?? TimeSpan.FromMilliseconds(200);
    }

    public static bool RpmRepeats(double first, double second)
    {
        if (first <= 0 || second <= 0)
        {
            return false;
        }

        return Math.Abs(first - second) <= Math.Max(50, first * 0.08);
    }

    public async Task<FanActuation> MeasureAsync(
        SafeFanSession session,
        FanGroup group,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(group);

        FanGroup live = Find(group.Id) ?? group;
        var points = new List<DutyRpmPoint>();
        double firstFull = 0;
        foreach (int duty in SweepDuties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DutySetResult write = session.TrySetDuty(group.Id, duty);
            if (!write.Accepted || session.IsAborted)
            {
                break;
            }

            double rpm = await ReadRpmAsync(group.Id, cancellationToken).ConfigureAwait(false);
            if (duty == 100 && firstFull <= 0)
            {
                firstFull = rpm;
            }

            points.Add(new DutyRpmPoint(duty, rpm));
        }

        int? minimumStable = points
            .Where(point => point.Rpm > 0)
            .Select(point => point.DutyPercent)
            .DefaultIfEmpty()
            .Min();
        if (minimumStable == 0)
        {
            minimumStable = null;
        }

        int? startDuty = await StartDutyAsync(session, group.Id, points, minimumStable, cancellationToken)
            .ConfigureAwait(false);

        bool repeats = false;
        if (firstFull > 0 && session.TrySetDuty(group.Id, 100).Accepted && !session.IsAborted)
        {
            double again = await ReadRpmAsync(group.Id, cancellationToken).ConfigureAwait(false);
            repeats = RpmRepeats(firstFull, again);
        }

        double? maximum = points.Count == 0 ? null : points.Max(point => point.Rpm);
        if (maximum is 0)
        {
            maximum = null;
        }

        return new FanActuation(
            group.Id,
            live.Name,
            live.DutyCyclePercent,
            live.Rpm,
            minimumStable,
            startDuty,
            maximum,
            repeats,
            points);
    }

    private async Task<int?> StartDutyAsync(
        SafeFanSession session,
        string fanGroupId,
        List<DutyRpmPoint> downward,
        int? minimumStable,
        CancellationToken cancellationToken)
    {
        if (minimumStable is null || downward.All(point => point.Rpm > 0))
        {
            return null;
        }

        foreach (int duty in SweepDuties.OrderBy(static value => value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.TrySetDuty(fanGroupId, duty).Accepted || session.IsAborted)
            {
                return null;
            }

            double rpm = await ReadRpmAsync(fanGroupId, cancellationToken).ConfigureAwait(false);
            if (rpm > 0)
            {
                return duty > minimumStable ? duty : null;
            }
        }

        return null;
    }

    private async Task<double> ReadRpmAsync(string fanGroupId, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + _stepTimeout;
        double rpm = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rpm = Find(fanGroupId)?.Rpm ?? 0;
            if (rpm > 0 || _clock.GetUtcNow() >= deadline)
            {
                return rpm;
            }

            await _delay(_samplePeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private FanGroup? Find(string id) =>
        _hardware.ReadSnapshot().FanGroups.FirstOrDefault(fan =>
            string.Equals(fan.Id, id, StringComparison.Ordinal));
}
