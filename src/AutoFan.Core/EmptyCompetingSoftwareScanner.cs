namespace AutoFan.Core;

public sealed class EmptyCompetingSoftwareScanner : ICompetingSoftwareScanner
{
    public static EmptyCompetingSoftwareScanner Instance { get; } = new();

    public IReadOnlyList<string> DetectRunning() => [];
}
