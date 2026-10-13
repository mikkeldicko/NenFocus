using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.EventHandlers;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractRecap;
using NenFocus.Domain.Events;
using NSubstitute;

namespace NenFocus.Tests.Application.EventHandlers;

/// <summary>Gap 110: an ended contract's recap reaches the Shell through this one handler.</summary>
public class NenContractEndedEventHandlerTests
{
    private readonly IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?> _recap =
        Substitute.For<IQueryHandler<GetNenContractRecapQuery, NenContractRecapDto?>>();
    private readonly IRecapRelay _relay = Substitute.For<IRecapRelay>();
    private readonly Guid _contractId = Guid.NewGuid();

    private IDomainEventHandler CreateHandler() => new NenContractEndedEventHandler(_recap, _relay);

    public NenContractEndedEventHandlerTests() =>
        _recap.HandleAsync(new GetNenContractRecapQuery(_contractId), Arg.Any<CancellationToken>())
            .Returns(new NenContractRecapDto { ContractId = _contractId });

    // Asked through the untyped door, as InProcessDomainEventPublisher asks.
    public static TheoryData<string> Endings => ["Completed", "Broken"];

    [Theory]
    [MemberData(nameof(Endings))]
    public async Task AnEndedContract_HandsItsRecapToTheShell(string ending)
    {
        DomainEvent ended = ending == "Completed"
            ? new NenContractCompletedEvent(_contractId, At.Utc(2026, 1, 8))
            : new NenContractBrokenEvent(_contractId, At.Utc(2026, 1, 8));

        await CreateHandler().HandleAsync(ended);

        _relay.Received(1).NotifyPostContractsReady(
            Arg.Is<IReadOnlyList<NenContractRecapDto>>(r => r.Single().ContractId == _contractId));
    }

    [Fact]
    public async Task AContractWithNoRecap_ShowsNothing()
    {
        _recap.HandleAsync(Arg.Any<GetNenContractRecapQuery>(), Arg.Any<CancellationToken>()).Returns((NenContractRecapDto?)null);

        await CreateHandler().HandleAsync(new NenContractBrokenEvent(_contractId, At.Utc(2026, 1, 8)));

        _relay.DidNotReceive().NotifyPostContractsReady(Arg.Any<IReadOnlyList<NenContractRecapDto>>());
    }
}
