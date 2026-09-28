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
        var localNow = _clock.ToLocal(now);

        // Only a handful of contracts are ever in effect — the entity decides which are due,
        // so the rule lives in exactly one place (IsDueForActivation / IsDueForCompletion).
        var inEffect = await _contracts.GetAllInEffectAsync(ct);

        var activatedIds = new List<Guid>();
        foreach (var contract in inEffect.Where(c => c.IsDueForActivation(localNow)))
        {
            contract.Activate(now);
            await _contracts.SaveAsync(contract, ct);
            activatedIds.Add(contract.Id);
        }

        // Checked after activation, so a contract whose whole period has already passed
        // (e.g. the app was closed throughout) activates and completes in the same sweep.
        var completedIds = new List<Guid>();
        foreach (var contract in inEffect.Where(c => c.IsDueForCompletion(localNow)))
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
