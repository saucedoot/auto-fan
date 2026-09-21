namespace AutoFan.Core;

public interface IPolicyConfirmationStore
{
    void Save(PolicyConfirmation confirmation);

    PolicyConfirmation? GetLatest();

    IReadOnlyList<PolicyConfirmation> List();
}
