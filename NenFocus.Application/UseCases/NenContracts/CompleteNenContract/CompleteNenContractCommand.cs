using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.CompleteNenContract;

/// <summary>
/// Lets the user close out an Active contract once every committed hour is delivered —
/// the Planner's "Complete Contract" button, next to Cover Deficit. Completing this way,
/// before EndDate passes on its own, is the normal path: waiting for the passive
/// EndDate-based completion to fire would otherwise mean sitting idle until midnight on
/// the contract's last day. NenContract.Complete(DateTime) already has no date check of
/// its own (only Status == Active is required); the readiness gate lives entirely in
/// CompleteNenContractValidator, not the Domain method.
/// </summary>
public sealed record CompleteNenContractCommand : ICommand<CompleteNenContractResult>
{
    public Guid ContractId { get; init; }
}
