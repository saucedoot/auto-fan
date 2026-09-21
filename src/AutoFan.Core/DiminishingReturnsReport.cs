namespace AutoFan.Core;

public sealed record DiminishingReturnsReport(IReadOnlyList<DiminishingReturnsBand> Bands)
{
    public bool HasRecommendation
    {
        get
        {
            foreach (DiminishingReturnsBand band in Bands)
            {
                if (band.RecommendedRpm is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
