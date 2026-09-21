namespace AutoFan.Core;

public static class FanWriteCandidates
{
    public static bool IsGpuHeader(FanGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return Contains(group.Id, "gpu")
            || Contains(group.ControllerName, "gpu")
            || Contains(group.ControllerName, "nvidia")
            || Contains(group.ControllerName, "amd")
            || Contains(group.ControllerName, "radeon")
            || Contains(group.Name, "gpu");
    }

    public static bool LooksAmdOrIntelGpu(string? controllerName, string? name)
    {
        return LooksAmd(controllerName, name) || LooksIntel(controllerName, name);
    }

    public static bool IsWritableNvidiaGpuFan(FanGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Kind == FanGroupKind.Fan
            && group.IsControllable
            && IsGpuHeader(group)
            && !LooksAmdOrIntelGpu(group.ControllerName, group.Name);
    }

    public static bool IsWritableMotherboardFan(FanGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Kind == FanGroupKind.Fan
            && group.IsControllable
            && !IsGpuHeader(group);
    }

    public static bool IsWritableTestFan(FanGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return IsWritableMotherboardFan(group) || IsWritableNvidiaGpuFan(group);
    }

    public static bool HasTachometer(FanGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Rpm is > 0;
    }

    public static bool HasUsableTachometer(FanGroup group, FanPresence? presence)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (presence is { Completed: true })
        {
            if (presence.IsEmpty(group.Id))
            {
                return false;
            }

            if (presence.IsConnected(group.Id))
            {
                return true;
            }
        }

        return HasTachometer(group);
    }

    public static string? CoupledSetKey(FanGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (!IsWritableNvidiaGpuFan(group))
        {
            return null;
        }

        if (GpuInstancePrefix(group.Id) is string prefix)
        {
            return prefix;
        }

        if (!string.IsNullOrWhiteSpace(group.ControllerName))
        {
            return "nvidia:" + group.ControllerName;
        }

        return group.Id;
    }

    public static bool AreCoupled(FanGroup first, FanGroup second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        string? key = CoupledSetKey(first);
        return key is not null && string.Equals(key, CoupledSetKey(second), StringComparison.Ordinal);
    }

    public static IReadOnlyList<FanGroup> MembersOfCoupledSet(
        FanGroup representative,
        IReadOnlyList<FanGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(representative);
        ArgumentNullException.ThrowIfNull(groups);
        string? key = CoupledSetKey(representative);
        if (key is null)
        {
            return [representative];
        }

        return groups
            .Where(item => string.Equals(CoupledSetKey(item), key, StringComparison.Ordinal))
            .ToArray();
    }

    public static IReadOnlyList<FanGroup> TakeOnePerCoupledSet(
        IReadOnlyList<FanGroup> groups,
        FanPresence? presence = null)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var kept = new List<FanGroup>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (FanGroup group in groups)
        {
            string? key = CoupledSetKey(group);
            if (key is null)
            {
                kept.Add(group);
                continue;
            }

            if (!seen.Add(key))
            {
                continue;
            }

            FanGroup? withTach = groups.FirstOrDefault(item =>
                string.Equals(CoupledSetKey(item), key, StringComparison.Ordinal)
                && HasUsableTachometer(item, presence));
            kept.Add(withTach ?? group);
        }

        return kept;
    }

    private static string? GpuInstancePrefix(string id)
    {
        int control = id.LastIndexOf("/control/", StringComparison.OrdinalIgnoreCase);
        int fan = id.LastIndexOf("/fan/", StringComparison.OrdinalIgnoreCase);
        int index = Math.Max(control, fan);
        return index > 0 ? id[..index] : null;
    }

    private static bool LooksAmd(string? controllerName, string? name) =>
        Contains(controllerName, "amd")
        || Contains(controllerName, "radeon")
        || Contains(name, "amd")
        || Contains(name, "radeon");

    private static bool LooksIntel(string? controllerName, string? name) =>
        Contains(controllerName, "intel")
        || Contains(controllerName, "uhd")
        || Contains(controllerName, "iris")
        || Contains(controllerName, "arc")
        || Contains(name, "intel")
        || Contains(name, "uhd")
        || Contains(name, "iris")
        || Contains(name, "arc");

    private static bool Contains(string? value, string token) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
