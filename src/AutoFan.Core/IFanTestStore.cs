namespace AutoFan.Core;

public interface IFanTestStore
{
    void Save(FanTestRun run);

    FanTestRun? GetLatest();

    IReadOnlyList<FanTestRun> List();
}
