using AutoFan.Core;

namespace AutoFan.Storage;

public sealed class InMemoryPolicyConfirmationStore : IPolicyConfirmationStore
{
    private readonly List<PolicyConfirmation> _rows = [];
    private readonly object _gate = new();

    public void Save(PolicyConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        lock (_gate)
        {
            _rows.Add(WithIdentity(confirmation));
        }
    }

    public PolicyConfirmation? GetLatest()
    {
        lock (_gate)
        {
            return _rows.Count == 0 ? null : _rows[^1];
        }
    }

    public IReadOnlyList<PolicyConfirmation> List()
    {
        lock (_gate)
        {
            return _rows.ToArray();
        }
    }

    internal static PolicyConfirmation WithIdentity(PolicyConfirmation confirmation) =>
        confirmation with
        {
            Id = confirmation.Id ?? Guid.NewGuid(),
            ObservedAt = confirmation.ObservedAt ?? DateTimeOffset.UtcNow,
        };
}
