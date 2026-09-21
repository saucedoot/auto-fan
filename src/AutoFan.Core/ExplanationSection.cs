namespace AutoFan.Core;

public sealed record ExplanationSection
{
    public ExplanationSection(string heading, IReadOnlyList<ExplanationClaim> claims)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(heading);
        ArgumentNullException.ThrowIfNull(claims);
        if (claims.Count == 0)
        {
            throw new ArgumentException("A section needs at least one tagged claim.", nameof(claims));
        }

        Heading = heading;
        Claims = claims;
    }

    public string Heading { get; }

    public IReadOnlyList<ExplanationClaim> Claims { get; }
}
