using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeRedemptionAttribution;
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

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult> _attribution =
        Substitute.For<IQueryHandler<ComputeRedemptionAttributionQuery, RedemptionAttributionResult>>();
    private readonly ISystemClock _clock = TestClock.Create();

    public GetBlockPlacementVerdictQueryHandlerTests()
    {
        _clock.SetNow(Now);
        _clock.SetTimeZoneOffset(TimeSpan.Zero);
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[]);
        // Default: no deficit found — the requested slot back, no contract picked up.
        _attribution.HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var query = (ComputeRedemptionAttributionQuery)ci[0];
                return new RedemptionAttributionResult(query.ContractId, [], query.Block);
            });
    }

    private GetBlockPlacementVerdictQueryHandler CreateHandler() =>
        new(_blocks, _sessions, _contracts, _attribution, new BlockPlacementChecker(), _clock);

    private static GetBlockPlacementVerdictQuery NewFocus(TimeSlot slot, Guid? contractId = null) =>
        new(slot, BlockPresetType.Focus, Guid.NewGuid(), contractId);

    private static NenContract NineToFiveContract(Guid id) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.DailyFlexibility,
            calendar: TestCalendars.For(DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now).AddDays(7),
                new DailyFloorRule.Fixed(DailyFloor.None), workHours: new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0))),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
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
            .Returns((IReadOnlyList<PlannedBlock>)[FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End))]);

        var result = await CreateHandler().HandleAsync(NewFocus(At.Slot(Start, End)));

        result.Outcome.Should().Be(PlacementOutcome.TimeTaken);
        result.Message.Should().Be("A Focus block already exists in this time window.");
    }

    [Fact]
    public async Task HandleAsync_OverlapsRunningSession_IsTimeTaken()
    {
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[RunningSession(Start.AddMinutes(-30), TimeSpan.FromHours(2))]);

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
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[session]);

        var result = await CreateHandler().HandleAsync(NewFocus(At.Slot(Start, End)));

        result.Outcome.Should().Be(PlacementOutcome.Allowed);
    }

    // ── Which contract applies ───────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_NewFocusBlock_ContractFoundThroughAttribution_ChecksItsHours()
    {
        // No ContractId on the query — the contract comes from a deficit attribution found.
        var contractId = Guid.NewGuid();
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(NineToFiveContract(contractId));
        var evening = At.Slot(Now.Date.AddHours(20), Now.Date.AddHours(21));
        _attribution.HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(contractId, [new(Guid.NewGuid(), 60)], evening));

        var result = await CreateHandler().HandleAsync(NewFocus(evening));

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
        var contractId = Guid.NewGuid();
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(NineToFiveContract(contractId));

        var result = await CreateHandler().HandleAsync(NewFocus(At.Slot(Start, End), contractId)); // 14:00-15:00

        result.Outcome.Should().Be(PlacementOutcome.Allowed);
    }

    [Fact]
    public async Task HandleAsync_ExistingBlock_KeepsItsOwnContract_AttributionScopedToIt()
    {
        var contractId = Guid.NewGuid();
        var blockId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(NineToFiveContract(contractId));
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, presetId, At.Slot(Start, End), contractId: contractId));
        var evening = At.Slot(Now.Date.AddHours(20), Now.Date.AddHours(21));

        var result = await CreateHandler().HandleAsync(
            new GetBlockPlacementVerdictQuery(evening, BlockPresetType.Focus, presetId, ExistingBlockId: blockId));

        result.Outcome.Should().Be(PlacementOutcome.OutsideContractHours);
        await _attribution.Received(1).HandleAsync(
            Arg.Is<ComputeRedemptionAttributionQuery>(q => q.ContractId == contractId && q.ExcludeBlockId == blockId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExistingBlockWithoutContract_NeverConsultsAttribution()
    {
        var blockId = Guid.NewGuid();
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>())
            .Returns(FocusBlock.Create(blockId, Guid.NewGuid(), At.Slot(Start, End)));

        await CreateHandler().HandleAsync(
            new GetBlockPlacementVerdictQuery(At.Slot(Start, End), BlockPresetType.Focus, Guid.NewGuid(), ExistingBlockId: blockId));

        await _attribution.DidNotReceive().HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>());
    }

    // ── The calendar and slot checked ────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ExistingBlock_LeavesItselfOutOfTheCalendar()
    {
        var blockId = Guid.NewGuid();
        var original = FocusBlock.Create(blockId, Guid.NewGuid(), At.Slot(Start, End));
        _blocks.GetByIdAsync(blockId, Arg.Any<CancellationToken>()).Returns(original);
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[original]);

        // Moved 30 min later — overlaps where it is now, which must not count against it.
        var result = await CreateHandler().HandleAsync(new GetBlockPlacementVerdictQuery(
            At.Slot(Start.AddMinutes(30), End.AddMinutes(30)), BlockPresetType.Focus, original.BlockPresetId, ExistingBlockId: blockId));

        result.Outcome.Should().Be(PlacementOutcome.Allowed);
    }

    [Fact]
    public async Task HandleAsync_RedemptionCapped_ChecksTheCappedSlotNotTheRequestedOne()
    {
        // Requested 14:00-16:00, but only 45 min of deficit exists, so 14:00-14:45 is what gets
        // saved. A Focus block at 15:00 sits only in the part that will be cut off.
        var contractId = Guid.NewGuid();
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(NineToFiveContract(contractId));
        var capped = At.Slot(Start, Start.AddMinutes(45));
        _attribution.HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RedemptionAttributionResult(contractId, [new(Guid.NewGuid(), 45)], capped, true));
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((TimeSlot)ci[0]).Overlaps(At.Slot(End, End.AddHours(1)))
                ? (IReadOnlyList<PlannedBlock>)[FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(End, End.AddHours(1)))]
                : []);

        var result = await CreateHandler().HandleAsync(NewFocus(At.Slot(Start, Start.AddHours(2))));

        result.Outcome.Should().Be(PlacementOutcome.Allowed);
        await _blocks.Received(1).GetForRangeAsync(capped, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BackgroundBlock_MayOverlapFocusTime_AndNeverConsultsAttribution()
    {
        _blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Start, End))]);

        var result = await CreateHandler().HandleAsync(
            new GetBlockPlacementVerdictQuery(At.Slot(Start, End), BlockPresetType.Background, Guid.NewGuid()));

        result.Outcome.Should().Be(PlacementOutcome.Allowed);
        await _attribution.DidNotReceive().HandleAsync(Arg.Any<ComputeRedemptionAttributionQuery>(), Arg.Any<CancellationToken>());
    }
}
