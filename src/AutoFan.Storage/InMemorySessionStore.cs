using AutoFan.Core;

namespace AutoFan.Storage;

/// <summary>
/// Process-lifetime store for dummy session results. Baseline runs use SQLite.
/// </summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly List<DummySessionResult> _results = [];
    private readonly object _gate = new();

    public void Save(DummySessionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            _results.Add(result);
        }
    }

    public DummySessionResult? GetLatest()
    {
        lock (_gate)
        {
            if (_results.Count == 0)
            {
                return null;
            }

            return _results[^1];
        }
    }

    public IReadOnlyList<DummySessionResult> List()
    {
        lock (_gate)
        {
            return _results.ToArray();
        }
    }
}
