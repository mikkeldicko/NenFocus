using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractRecap;
using NenFocus.Domain.Events;

namespace NenFocus.Application.EventHandlers;

/// <summary>
/// A contract ended — completed (by the lifecycle sweep, or early by the user) or broken — so its
/// Post-Contract recap is owed. The one path to that screen (Gap 110): events are published after the
/// commit, so the recap reads what was saved, never a command's unsaved changes.
/// </summary>
public sealed class NenContractEndedEventHandler
    : IDomainEventHandler<NenContractCompletedEvent>, IDomainEventHandler<NenContractBrokenEvent>
{
    private readonly IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> _recap;
    private readonly IRecapRelay _recaps;

    public NenContractEndedEventHandler(
        IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> recap,
        IRecapRelay recaps)
    {
        _recap = recap;
        _recaps = recaps;
    }

    public Task HandleAsync(NenContractCompletedEvent domainEvent, CancellationToken cancellationToken = default) =>
        ShowRecapAsync(domainEvent.ContractId, cancellationToken);

    public Task HandleAsync(NenContractBrokenEvent domainEvent, CancellationToken cancellationToken = default) =>
        ShowRecapAsync(domainEvent.ContractId, cancellationToken);

    // Handling two events, the class names the untyped door itself (each generic interface offers one).
    Task IDomainEventHandler.HandleAsync(DomainEvent domainEvent, CancellationToken cancellationToken) => domainEvent switch
    {
        NenContractCompletedEvent completed => HandleAsync(completed, cancellationToken),
        NenContractBrokenEvent broken => HandleAsync(broken, cancellationToken),
        _ => throw new ArgumentException($"Not a contract-ended event: {domainEvent.GetType().Name}", nameof(domainEvent))
    };

    private async Task ShowRecapAsync(Guid contractId, CancellationToken cancellationToken)
    {
        if (await _recap.HandleAsync(new GetNenContractRecapQuery(contractId), cancellationToken) is { } recap)
            _recaps.NotifyPostContractsReady([recap]);
    }
}
