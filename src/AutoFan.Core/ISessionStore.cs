namespace AutoFan.Core;

public interface ISessionStore
{
    void Save(DummySessionResult result);

    DummySessionResult? GetLatest();

    IReadOnlyList<DummySessionResult> List();
}
