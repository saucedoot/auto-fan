using AutoFan.Core;

namespace AutoFan.Storage;

public sealed class InMemoryFanTestStore : IFanTestStore
{
    private readonly List<FanTestRun> _runs = [];
    private readonly object _gate = new();

    public void Save(FanTestRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        lock (_gate)
        {
            _runs.Add(run);
        }
    }

    public FanTestRun? GetLatest()
    {
        lock (_gate)
        {
            return _runs.Count == 0 ? null : _runs[^1];
        }
    }

    public IReadOnlyList<FanTestRun> List()
    {
        lock (_gate)
        {
            return _runs.ToArray();
        }
    }
}
