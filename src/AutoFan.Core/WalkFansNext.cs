namespace AutoFan.Core;

/// <summary>
/// What the Optimize walk does after individual fan tests.
/// </summary>
public enum WalkFansNext
{
    Retry,
    RunPairs,
    SkipPairs,
}
