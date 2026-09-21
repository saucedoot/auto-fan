namespace AutoFan.Core;

public interface IInteractionStore
{
    void Save(InteractionRun run);

    InteractionRun? GetLatest();

    IReadOnlyList<InteractionRun> List();
}
