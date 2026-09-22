using AutoFan.Core;

namespace AutoFan.Storage;

public sealed class InMemoryBaselineStore : IBaselineStore
{
    private readonly List<BaselineRun> _runs = [];
    private readonly object _gate = new();

    public void Save(BaselineRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        lock (_gate)
        {
            _runs.Add(run);
        }
    }

    public BaselineRun? GetLatest()
    {
        lock (_gate)
        {
            return _runs.Count == 0 ? null : _runs[^1];
        }
    }

    public IReadOnlyList<BaselineRun> List()
    {
        lock (_gate)
        {
            return _runs.ToArray();
        }
    }

    public void UpdateHotProfile(HeatProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_gate)
        {
            if (_runs.Count == 0)
            {
                return;
            }

            _runs[^1] = _runs[^1] with { HotProfile = profile };
        }
    }
}
