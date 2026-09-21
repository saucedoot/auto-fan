using AutoFan.Core;

namespace AutoFan.Storage;

public sealed class InMemoryInteractionStore : IInteractionStore
{
    private readonly List<InteractionRun> _runs = [];
    private readonly object _gate = new();

    public void Save(InteractionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        lock (_gate)
        {
            _runs.Add(run);
        }
    }

    public InteractionRun? GetLatest()
    {
        lock (_gate)
        {
            return _runs.Count == 0 ? null : _runs[^1];
        }
    }

    public IReadOnlyList<InteractionRun> List()
    {
        lock (_gate)
        {
            return _runs.ToArray();
        }
    }
}
