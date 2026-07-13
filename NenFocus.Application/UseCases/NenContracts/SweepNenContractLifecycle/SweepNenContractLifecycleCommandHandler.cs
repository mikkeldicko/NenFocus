using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.SweepNenContractLifecycle;

public sealed class SweepNenContractLifecycleCommandHandler
    : ICommandHandler<SweepNenContractLifecycleCommand, SweepNenContractLifecycleResult>
{
    private readonly INenContractRepository _contracts;
    private readonly ISystemClock _clock;

    public SweepNenContractLifecycleCommandHandler(INenContractRepository contracts, ISystemClock clock)
    {
        _contracts = contracts;
        _clock = clock;
    }

    public async Task<SweepNenContractLifecycleResult> HandleAsync(
        SweepNenContractLifecycleCommand command,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var dueForActivation = await _contracts.GetDueForActivationAsync(now, ct);
        var activatedIds = new List<Guid>();
        foreach (var contract in dueForActivation)
        {
            contract.Activate(now);
            await _contracts.SaveAsync(contract, ct);
            activatedIds.Add(contract.Id);
        }

        var dueForCompletion = await _contracts.GetDueForCompletionAsync(now, ct);
        var completedIds = new List<Guid>();
        foreach (var contract in dueForCompletion)
        {
            contract.Complete(now);
            await _contracts.SaveAsync(contract, ct);
            completedIds.Add(contract.Id);
        }

        return new SweepNenContractLifecycleResult
        {
            ActivatedContractIds = activatedIds,
            CompletedContractIds = completedIds
        };
    }
}
