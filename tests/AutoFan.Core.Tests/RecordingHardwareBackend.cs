using AutoFan.Core;

namespace AutoFan.Core.Tests;

internal sealed class RecordingHardwareBackend : IHardwareBackend
{
    private readonly IHardwareBackend _inner;

    public RecordingHardwareBackend(IHardwareBackend inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public List<string> Events { get; } = [];

    public string DisplayName => _inner.DisplayName;

    public bool IsDemo => _inner.IsDemo;

    public IReadOnlyList<FanGroup> FanGroups => _inner.FanGroups;

    public bool HasActiveSoftwareControl => _inner.HasActiveSoftwareControl;

    public HardwareSnapshot ReadSnapshot() => _inner.ReadSnapshot();

    public DutySetResult TrySetDuty(string fanGroupId, int percent)
    {
        DutySetResult result = _inner.TrySetDuty(fanGroupId, percent);
        Events.Add(result.Accepted ? $"write:{fanGroupId}:{percent}" : $"reject:{fanGroupId}");
        return result;
    }

    public void RestoreDefaults()
    {
        Events.Add("restore");
        _inner.RestoreDefaults();
    }
}
