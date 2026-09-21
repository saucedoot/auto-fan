using AutoFan.Core;

namespace AutoFan.Core.Tests;

internal sealed class ReorderedFanGroupsBackend : IHardwareBackend
{
    private readonly IHardwareBackend _inner;
    private readonly IReadOnlyList<string> _order;

    public ReorderedFanGroupsBackend(IHardwareBackend inner, IReadOnlyList<string> order)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _order = order ?? throw new ArgumentNullException(nameof(order));
    }

    public string DisplayName => _inner.DisplayName;

    public bool IsDemo => _inner.IsDemo;

    public IReadOnlyList<FanGroup> FanGroups => Order(_inner.FanGroups);

    public bool HasActiveSoftwareControl => _inner.HasActiveSoftwareControl;

    public HardwareSnapshot ReadSnapshot()
    {
        HardwareSnapshot snapshot = _inner.ReadSnapshot();
        return snapshot with { FanGroups = Order(snapshot.FanGroups) };
    }

    public DutySetResult TrySetDuty(string fanGroupId, int percent) =>
        _inner.TrySetDuty(fanGroupId, percent);

    public void RestoreDefaults() => _inner.RestoreDefaults();

    private IReadOnlyList<FanGroup> Order(IReadOnlyList<FanGroup> groups)
    {
        var sorted = new List<FanGroup>(groups.Count);
        foreach (string id in _order)
        {
            FanGroup? match = groups.FirstOrDefault(group => group.Id == id);
            if (match is not null)
            {
                sorted.Add(match);
            }
        }

        foreach (FanGroup group in groups)
        {
            if (sorted.All(existing => existing.Id != group.Id))
            {
                sorted.Add(group);
            }
        }

        return sorted;
    }
}
