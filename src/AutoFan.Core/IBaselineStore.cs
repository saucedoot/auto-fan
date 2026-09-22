namespace AutoFan.Core;

public interface IBaselineStore
{
    void Save(BaselineRun run);

    BaselineRun? GetLatest();

    IReadOnlyList<BaselineRun> List();

    /// <summary>
    /// Persist a kept Hot lamp on the latest Watch run. Missing Hot after a
    /// skip is correct — do not call this.
    /// </summary>
    void UpdateHotProfile(HeatProfile profile);
}
