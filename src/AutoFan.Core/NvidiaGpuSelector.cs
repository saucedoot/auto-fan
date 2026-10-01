namespace AutoFan.Core;

public readonly record struct NvidiaGpuChoice(uint GpuId, string Name);

/// <summary>
/// Picks the NVIDIA card a fan write is allowed to touch. A hardware id that
/// is missing from the list is a refusal. The same display name is not enough
/// when more than one card matches, and a named card is never replaced with
/// a different card.
/// </summary>
public static class NvidiaGpuSelector
{
    public static uint? Select(
        IReadOnlyList<NvidiaGpuChoice> gpus,
        string? hardwareId,
        string? displayName)
    {
        ArgumentNullException.ThrowIfNull(gpus);
        if (gpus.Count == 0)
        {
            return null;
        }

        if (uint.TryParse(hardwareId, out uint id))
        {
            return gpus.Any(gpu => gpu.GpuId == id) ? id : null;
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            NvidiaGpuChoice[] named = gpus.Where(gpu => NameMatches(gpu.Name, displayName)).ToArray();
            return named.Length == 1 ? named[0].GpuId : null;
        }

        return gpus.Count == 1 ? gpus[0].GpuId : null;
    }

    private static bool NameMatches(string fullName, string preferred) =>
        fullName.Contains(preferred, StringComparison.OrdinalIgnoreCase)
        || preferred.Contains(fullName, StringComparison.OrdinalIgnoreCase);
}
