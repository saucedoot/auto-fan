namespace AutoFan.Core;

/// <summary>
/// Detects other fan-control software that would fight AUTO Fan for headers.
/// </summary>
public interface ICompetingSoftwareScanner
{
    /// <summary>
    /// Display names of competing apps that are currently running.
    /// </summary>
    IReadOnlyList<string> DetectRunning();
}
