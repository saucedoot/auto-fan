namespace AutoFan.Core;

public sealed record ExplanationClaim
{
    public ExplanationClaim(string title, string text, MetricEvidence evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Title = title;
        Text = text;
        Evidence = evidence;
    }

    public string Title { get; }

    public string Text { get; }

    public MetricEvidence Evidence { get; }
}
