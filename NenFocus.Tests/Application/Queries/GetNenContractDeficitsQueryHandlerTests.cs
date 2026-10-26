using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

/// <summary>
/// A loader: the contract works out what is owed (<see cref="NenContract.Deficits"/>, tested in
/// <c>NenContractDeficitsTests</c>); this checks only what it loads and how it's shown (Gap 106).
/// </summary>
public class GetNenContractDeficitsQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IBlockRepository _blocks = Substitute.For<IBlockRepository>();
    private readonly ISystemClock _clock = TestClock.Create();
    private readonly NenContract _contract;

    public GetNenContractDeficitsQueryHandlerTests()
    {
        _clock.SetNow(Now);
        var monday = new DateOnly(2026, 1, 5);
        var rules = TestRules.Of(StrictnessRule.Locked, null);
        var plan = new Dictionary<DateOnly, double>();
        _contract = NenContract.Seal(Guid.NewGuid(), "Contract", new NenContractGoal("Goal"), rules,
            TestCalendars.For(monday, monday.AddDays(2), rules.FloorRule, plan), plan, At.Utc(2026, 1, 4, 12), At.UtcZone);
        _contracts.GetByIdAsync(_contract.Id, Arg.Any<CancellationToken>()).Returns(_contract);
    }

    private FocusBlock Committed(DateTime start, DateTime end) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(start, end), membership: TestContracts.Committed(_contract.Id));

    private void ContractHas(params Block[] blocks) =>
        _blocks.GetForContractAsync(_contract.Id, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Block>)blocks);

    private Task<IReadOnlyList<NenFocus.Application.DTOs.FocusBlockDeficitDto>> Handle(Guid contractId) =>
        new GetNenContractDeficitsQueryHandler(_contracts, _blocks, _clock)
            .HandleAsync(new GetNenContractDeficitsQuery(contractId), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_UnknownContract_ReturnsEmpty()
    {
        (await Handle(Guid.NewGuid())).Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_EndedEarlyBlock_ShowsPlannedCompletedAndRemaining()
    {
        // Started on time, quit after 45 of 120 minutes.
        var block = TestBlocks.Worked(Committed(Now.AddHours(-3), Now.AddHours(-1)), 0, 45);
        ContractHas(block);

        var dto = (await Handle(_contract.Id)).Should().ContainSingle().Subject;

        dto.FocusBlockId.Should().Be(block.Id);
        dto.BlockPresetId.Should().Be(block.BlockPresetId);
        dto.Scheduled.Should().Be(block.Scheduled);
        dto.Outcome.Should().Be(FocusBlockOutcome.EndedEarly);
        dto.RemainingDeficit.Should().Be(WholeMinutes.Of(75));
    }
}
