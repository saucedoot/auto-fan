namespace AutoFan.Core;

public sealed record CasePrior(string Id, IReadOnlyList<CasePathGuess> Paths)
{
    public InfluenceTarget? TargetOf(CaseZone zone)
    {
        foreach (CasePathGuess path in Paths)
        {
            if (path.Zone == zone)
            {
                return path.LikelyFirstTarget;
            }
        }

        return null;
    }
}
