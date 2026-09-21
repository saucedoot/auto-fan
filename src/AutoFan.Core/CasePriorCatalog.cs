namespace AutoFan.Core;

public static class CasePriorCatalog
{
    public static CasePrior GenericMidTower { get; } = new(
        "generic-mid-tower",
        [
            new CasePathGuess(CaseZone.Front, InfluenceTarget.Gpu),
            new CasePathGuess(CaseZone.Bottom, InfluenceTarget.Gpu),
            new CasePathGuess(CaseZone.Rear, InfluenceTarget.Cpu),
            new CasePathGuess(CaseZone.Top, InfluenceTarget.Cpu),
            new CasePathGuess(CaseZone.CpuHeader, InfluenceTarget.Cpu),
        ]);
}
