namespace AutoFan.Core;

public sealed record ExplanationReport
{
    public ExplanationReport(IReadOnlyList<ExplanationSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        Sections = sections;
    }

    public IReadOnlyList<ExplanationSection> Sections { get; }
}
