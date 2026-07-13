namespace NenFocus.Application.UseCases.NenContracts.SweepNenContractLifecycle;

public sealed record SweepNenContractLifecycleResult
{
    public IReadOnlyList<Guid> ActivatedContractIds { get; init; } = [];
    public IReadOnlyList<Guid> CompletedContractIds { get; init; } = [];
}
