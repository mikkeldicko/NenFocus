using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetBlockPlacementVerdict;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

// The rules themselves are BlockPlacementChecker's (BlockPlacementCheckerTests). Here: that the
// query hands the checker the right contract, calendar, sessions and slot, and owns the wording.
// Also holds the placement cases that used to live in the Create/Update handler and validator
// tests before Gap 88 phase 8 moved the check here.
public class GetBlockPlacementVerdictQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = Now.AddHours(2); // 14:00
    private static readonly DateTime End = Now.AddHours(3);   // 15:00

    private readonly IBlockRepository _blocks = Substitute.For<IBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly ISystemClock _clock = TestClock.Create();

    public GetBlockPlacementVerdictQueryHandlerTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Block>)[]);
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    private GetBlockPlacementVerdictQueryHandler CreateHandler() =>
        new(_blocks, _sessions, _contracts, new BlockPlacementChecker(), _clock);

    private static GetBlockPlacementVerdictQuery NewFocus(TimeSlot slot) =>
        new(slot, BlockPresetType.Focus, Guid.NewGuid());

    private static NenContract NineToFiveContract(Guid id) =>
        TestContracts.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.DailyFlexibility,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7),
                new DailyFloorRule.None(), workHours: new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0))),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Now.Date), timeZone: At.UtcZone);

    private static Session RunningSession(DateTime startedAt, TimeSpan duration)
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(duration),
            Guid.NewGuid(), SessionOrigin.Spontaneous);
        session.Start(At.Utc(startedAt));
        return session;
    }

    // ── Outcomes and wording ─────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_FreeFutureSlot_IsAllowedWithNoMessage()
    {
        var result = await CreateHandler().HandleAsync(NewFocus(At.Slot(Start, End)));

        result.Outcome.Should().Be(PlacementOutcome.Allowed);
        result.Message.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_StartInThePast_IsInThePast()
    {
        var result = await CreateHandler().HandleAsync(NewFocus(At.Slot(Now.AddMinutes(-10), End)));

        result.Outcome.Should().Be(PlacementOutcome.InThePast);
        result.Message.Should().Be("Start time must be in the future.");
    }

    [Fact]
    public async Task HandleAsync_OverlapsAnotherFocusBlock_IsTimeTaken()
    {
        _blocks.GetForRangeAsync(At.Slot(Start, End), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Block>)[FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End))]);

        var result = await CreateHandler().HandleAsync(NewFocus(At.Slot(Start, End)));

        result.Outcome.Should().Be(PlacementOutcome.TimeTaken);
        result.Message.Should().Be("A Focus block already exists in this time window.");
    }

    [Fact]
    public async Task HandleAsync_OverlapsRunningSession_IsTimeTaken()
    {
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns(RunningSession(Start.AddMinutes(-30), TimeSpan.FromHours(2)));

        var result = await CreateHandler().HandleAsync(NewFocus(At.Slot(Start, End)));

        result.Outcome.Should().Be(PlacementOutcome.TimeTaken);
    }

    [Fact]
    public async Task HandleAsync_PlannedSessionStartedLate_UsesItsRealEndNotStartedAtPlusDuration()
    {
        // A Planned session's end is pinned to its block's scheduled end, while Duration is the
        // block's full length. Started 5 min late, StartedAt + Duration would overshoot the real
        // end by 5 min — a block right after the real end must still fit (Gap 32).
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromHours(1)),
            Guid.NewGuid(), SessionOrigin.Planned, originatingBlockId: Guid.NewGuid());
        session.Start(At.Utc(Start.AddMinutes(-55)), plannedWindow: At.PlannedWindowEnding(Start));
        _sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(session);

        var result = await CreateHandler().HandleAsync(NewFocus(At.Slot(Start, End)));

        result.Outcome.Should().Be(PlacementOutcome.Allowed);
    }

    // ── Which contract applies ───────────────────────────────────────────────

    private Guid SetUpNineToFiveContractInEffect()
    {
        var contract = NineToFiveContract(Guid.NewGuid());
        _contracts.GetByIdAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract);
        _contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[contract]);
        return contract.Id;
    }

    [Theory]
    [InlineData(BlockPresetType.Focus)]
    [InlineData(BlockPresetType.Background)]
    public async Task HandleAsync_NewPlainBlock_InsideAContractInEffect_IsHeldToItsHours(BlockPresetType type)
    {
        // Gap 97: no ContractId, no deficit — the contract governs because the slot sits in its period.
        SetUpNineToFiveContractInEffect();
        var evening = At.Slot(Now.Date.AddHours(20), Now.Date.AddHours(21));

        var result = await CreateHandler().HandleAsync(new GetBlockPlacementVerdictQuery(evening, type, Guid.NewGuid()));

        result.Outcome.Should().Be(PlacementOutcome.OutsideContractHours);
        result.Message.Should().Be("This block falls outside the contract's configured Work Hours for that day.");
    }

    [Fact]
    public async Task HandleAsync_NewFocusBlock_NoContract_SkipsContractHours()
    {
        var evening = At.Slot(Now.Date.AddHours(20), Now.Date.AddHours(21));

        var result = await CreateHandler().HandleAsync(NewFocus(evening));

        result.Outcome.Should().Be(PlacementOutcome.Allowed);
    }

    [Fact]
    public async Task HandleAsync_NewFocusBlock_InsideContractHours_IsAllowed()
    {
        SetUpNineToFiveContractInEffect();

        var result = await CreateHandler().HandleAsync(NewFocus(At.Slot(Start, End))); // 14:00-15:00

        result.Outcome.Should().Be(PlacementOutcome.Allowed);
    }

    [Fact]
    public async Task HandleAsync_NewBlock_AfterTheContractsLastWindowCloses_IsAPlainAllowedBlock()
    {
        // The contract's last day is Now + 7 days, 09:00–17:00: 19:00 that day is after the period.
        SetUpNineToFiveContractInEffect();
        var lastEvening = At.Slot(Now.Date.AddDays(7).AddHours(19), Now.Date.AddDays(7).AddHours(20));

        var result = await CreateHandler().HandleAsync(NewFocus(lastEvening));

        result.Outcome.Should().Be(PlacementOutcome.Allowed);
    }

    [Fact]
    public async Task HandleAsync_ExistingCommittedBlock_MovedPastItsContractsEnd_IsRefused()
    {
        // The same slot a plain block may take: a Committed block never leaves its contract.
        var contractId = SetUpNineToFiveContractInEffect();
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, At.Slot(Start, End), membership: TestContracts.Committed(contractId)));
        var lastEvening = At.Slot(Now.Date.AddDays(7).AddHours(19), Now.Date.AddDays(7).AddHours(20));

        var result = await CreateHandler().HandleAsync(
            new GetBlockPlacementVerdictQuery(lastEvening, BlockPresetType.Focus, presetId, ExistingBlockId: blockId));

        result.Outcome.Should().Be(PlacementOutcome.OutsideContractHours);
    }

    [Fact]
    public async Task HandleAsync_ExistingRedemptionBlock_KeepsItsOwnContract()
    {
        var contractId = SetUpNineToFiveContractInEffect();
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, At.Slot(Start, End), membership: TestContracts.Redemption(contractId)));
        var evening = At.Slot(Now.Date.AddHours(20), Now.Date.AddHours(21));

        var result = await CreateHandler().HandleAsync(
            new GetBlockPlacementVerdictQuery(evening, BlockPresetType.Focus, presetId, ExistingBlockId: blockId));

        result.Outcome.Should().Be(PlacementOutcome.OutsideContractHours);
    }

    [Fact]
    public async Task HandleAsync_ExistingRedemptionBlock_CoveringLessThanItsLength_ChecksTheRequestedSlot()
    {
        // A move keeps the receipt and the length (Gap 106, decision 3), so Update saves the
        // requested slot — the one checked here, never one capped to the receipt (Rule 12).
        var contractId = SetUpNineToFiveContractInEffect();
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.CreateWithReceipt(blockId, presetId, At.Slot(Start, End), title: null, membership: TestContracts.Redemption(contractId),
                receipt: new RedemptionReceipt([new RedemptionEntry(Guid.NewGuid(), WholeMinutes.Of(30))])));
        var requested = At.Slot(Start.AddHours(1), End.AddHours(1));

        await CreateHandler().HandleAsync(new GetBlockPlacementVerdictQuery(requested, BlockPresetType.Focus, presetId, ExistingBlockId: blockId));

        await _blocks.Received(1).GetForRangeAsync(requested, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NewFocusBlock_InsideAContract_ChecksTheRequestedSlot()
    {
        // A new block never covers a deficit (only Cover Deficit does, Gap 106): it is saved
        // uncapped, so it is checked uncapped.
        SetUpNineToFiveContractInEffect();
        var requested = At.Slot(Start, Start.AddHours(2));

        await CreateHandler().HandleAsync(NewFocus(requested));

        await _blocks.Received(1).GetForRangeAsync(requested, Arg.Any<CancellationToken>());
    }


    // ── The calendar and slot checked ────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ExistingBlock_LeavesItselfOutOfTheCalendar()
    {
        var blockId = Guid.NewGuid();
        var original = FocusBlock.Create(blockId, Guid.NewGuid(), At.Slot(Start, End));
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(original);
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Block>)[original]);

        // Moved 30 min later — overlaps where it is now, which must not count against it.
        var result = await CreateHandler().HandleAsync(new GetBlockPlacementVerdictQuery(
            At.Slot(Start.AddMinutes(30), End.AddMinutes(30)), BlockPresetType.Focus, original.BlockPresetId, ExistingBlockId: blockId));

        result.Outcome.Should().Be(PlacementOutcome.Allowed);
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_MayOverlapFocusTime()
    {
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Block>)[FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End))]);

        var result = await CreateHandler().HandleAsync(
            new GetBlockPlacementVerdictQuery(At.Slot(Start, End), BlockPresetType.Background, Guid.NewGuid()));

        result.Outcome.Should().Be(PlacementOutcome.Allowed);
    }
}
