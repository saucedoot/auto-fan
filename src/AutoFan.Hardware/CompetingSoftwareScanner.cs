using System.Diagnostics;
using AutoFan.Core;

namespace AutoFan.Hardware;

public sealed class CompetingSoftwareScanner : ICompetingSoftwareScanner
{
    private readonly Func<IReadOnlyList<string>> _processNames;

    public CompetingSoftwareScanner()
        : this(static () => Process.GetProcesses().Select(static process => process.ProcessName).ToArray())
    {
    }

    public CompetingSoftwareScanner(Func<IReadOnlyList<string>> processNames)
    {
        _processNames = processNames ?? throw new ArgumentNullException(nameof(processNames));
    }

    public IReadOnlyList<string> DetectRunning() => CompetingSoftwareCatalog.Match(_processNames());
}
