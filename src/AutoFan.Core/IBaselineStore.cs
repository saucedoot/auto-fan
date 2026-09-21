namespace AutoFan.Core;

public interface IBaselineStore
{
    void Save(BaselineRun run);

    BaselineRun? GetLatest();

    IReadOnlyList<BaselineRun> List();
}
