using FluentAssertions;
using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractDeficits;
using NenFocus.Application.Queries.GetSuggestedRedemptionPlacements;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetSuggestedRedemptionPlacementsQueryHandlerTests
{
    private static readonly DateOnly Today = new(2026, 1, 5);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>> _deficits =
        Substitute.For<IQueryHandler<GetNenContractDeficitsQuery, IReadOnlyList<FocusBlockDeficitDto>>>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    // Local time = UTC unless a test moves the user to another timezone (UseUtcOffset).
    public GetSuggestedRedemptionPlacementsQueryHandlerTests() => UseUtcOffset(TimeSpan.Zero);

    private void UseUtcOffset(TimeSpan offset)
    {
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>() + offset);
        _clock.ToUtc(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>() - offset);
    }

    private GetSuggestedRedemptionPlacementsQueryHandler CreateHandler() => new(_contracts, _deficits, _presets, _blocks, _clock);

    private static NenContract SameDayContract(Guid id, WorkHours? workHours = null) =>
        NenContract.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(Today.AddDays(-1), Today.AddDays(7), new DailyFloorRule.Fixed(DailyFloor.None), workHours: workHours),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            localSealedAt: Today.AddDays(-1).ToDateTime(TimeOnly.MinValue),
            redemptionPlacementPolicy: RedemptionPlacementPolicy.CurrentDayOnly);

    private static FocusBlockDeficitDto Deficit(Guid presetId, DateTime scheduledStart, double remaining) =>
        new()
        {
            FocusBlockId = Guid.NewGuid(),
            BlockPresetId = presetId,
            ScheduledStart = scheduledStart,
            ScheduledEnd = scheduledStart.AddHours(1),
            Outcome = FocusBlockOutcome.Missed,
            RemainingDeficitMinutes = remaining,
            PlannedMinutes = 60,
            CompletedMinutes = 0
        };

    private void NoConflicts() =>
        _blocks.GetForDayAsync(Today, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);

    [Fact]
    public async Task HandleAsync_ContractNotFound_ReturnsEmpty()
    {
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(Guid.NewGuid(), Today.ToDateTime(new TimeOnly(10, 0))), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_NoDeficits_ReturnsEmpty()
    {
        var contractId = Guid.NewGuid();
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[]);

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, Today.ToDateTime(new TimeOnly(10, 0))), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_SameDayContract_DeficitMissedYesterday_StillCoverableToday()
    {
        // The bug this feature exists to fix: the Cover Deficit button must stay available
        // today for a deficit from any earlier day, under CurrentDayOnly — eligibility no
        // longer compares against the missed block's own date at all.
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        var deficit = Deficit(presetId, Today.AddDays(-1).ToDateTime(new TimeOnly(10, 0)), remaining: 30);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Today.ToDateTime(TimeOnly.MinValue)));
        NoConflicts();

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, Today.ToDateTime(new TimeOnly(10, 0))), CancellationToken.None);

        result.Should().ContainSingle(p => p.PresetId == presetId && p.TotalRemainingDeficitMinutes == 30);
    }

    [Fact]
    public async Task HandleAsync_AnywhereInContractPolicy_DeficitMissedYesterday_CoverableToday()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = NenContract.Seal(
            id: contractId,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(Today.AddDays(-1), Today.AddDays(7), new DailyFloorRule.Fixed(DailyFloor.None)),
            floorRule: new DailyFloorRule.Fixed(DailyFloor.None),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            localSealedAt: Today.AddDays(-1).ToDateTime(TimeOnly.MinValue),
            redemptionPlacementPolicy: RedemptionPlacementPolicy.AnywhereInContract);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        var deficit = Deficit(presetId, Today.AddDays(-1).ToDateTime(new TimeOnly(10, 0)), remaining: 30);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, Today.ToDateTime(TimeOnly.MinValue)));
        NoConflicts();

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, Today.ToDateTime(new TimeOnly(10, 0))), CancellationToken.None);

        result.Should().ContainSingle(p => p.PresetId == presetId && p.TotalRemainingDeficitMinutes == 30);
    }

    [Fact]
    public async Task HandleAsync_PlentyOfRoom_SnapsToNextQuarterHour_CanFullyCover()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var now = Today.ToDateTime(new TimeOnly(10, 7)); // 7 min past the hour
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        var deficit = Deficit(presetId, now, remaining: 40);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, now));
        NoConflicts();

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, now), CancellationToken.None);

        result.Should().ContainSingle();
        var placement = result[0];
        placement.PresetId.Should().Be(presetId);
        placement.PresetName.Should().Be("Deep Work");
        placement.TotalRemainingDeficitMinutes.Should().Be(40);
        placement.SuggestedStart.Should().Be(Today.ToDateTime(new TimeOnly(10, 15)));
        placement.SuggestedEnd.Should().Be(Today.ToDateTime(new TimeOnly(10, 55)));
        placement.CanFullyCover.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NearMidnight_ShiftsStartEarlierToStillFitFully()
    {
        // The exact worked example from design: 50 minutes owed at 23:05 — snapping to
        // 23:15 would leave only 45 minutes before midnight, so the start shifts back to
        // 23:10 instead, letting the full 50 minutes land exactly at midnight.
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var now = Today.ToDateTime(new TimeOnly(23, 5));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        var deficit = Deficit(presetId, now, remaining: 50);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, now));
        NoConflicts();

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, now), CancellationToken.None);

        var placement = result.Should().ContainSingle().Subject;
        placement.SuggestedStart.Should().Be(Today.ToDateTime(new TimeOnly(23, 10)));
        placement.SuggestedEnd.Should().Be(Today.AddDays(1).ToDateTime(TimeOnly.MinValue));
        placement.CanFullyCover.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_CannotFitEvenStartingNow_FallsBackToPartialCoverage()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var now = Today.ToDateTime(new TimeOnly(23, 50)); // only 10 minutes left before midnight
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        var deficit = Deficit(presetId, now, remaining: 40);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, now));
        NoConflicts();

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, now), CancellationToken.None);

        var placement = result.Should().ContainSingle().Subject;
        placement.SuggestedStart.Should().Be(now);
        placement.SuggestedEnd.Should().Be(Today.AddDays(1).ToDateTime(TimeOnly.MinValue));
        placement.CanFullyCover.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_IdealSlotConflictsWithExistingBlock_SearchesForwardPastIt()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var now = Today.ToDateTime(new TimeOnly(10, 7));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        var deficit = Deficit(presetId, now, remaining: 30);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, now));

        // Occupies exactly the ideal 10:15-10:45 slot the 30-minute deficit would otherwise land in.
        var conflicting = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            Today.ToDateTime(new TimeOnly(10, 15)), Today.ToDateTime(new TimeOnly(10, 45)));
        _blocks.GetForDayAsync(Today, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[conflicting]);

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, now), CancellationToken.None);

        var placement = result.Should().ContainSingle().Subject;
        placement.SuggestedStart.Should().Be(Today.ToDateTime(new TimeOnly(10, 45)));
        placement.SuggestedEnd.Should().Be(Today.ToDateTime(new TimeOnly(11, 15)));
        placement.CanFullyCover.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WorkHoursCloseEarlierThanMidnight_UsesItAsTheBoundary()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var now = Today.ToDateTime(new TimeOnly(16, 47));
        var workHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId, workHours));
        var deficit = Deficit(presetId, now, remaining: 10);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, now));
        NoConflicts();

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, now), CancellationToken.None);

        // Snapping to 17:00 would leave zero room before the 17:00 Work Hours close, so the
        // start shifts back to exactly 16:50 (10 minutes before the boundary) — still after
        // "now" (16:47), so no partial-coverage fallback is needed.
        var placement = result.Should().ContainSingle().Subject;
        placement.SuggestedStart.Should().Be(Today.ToDateTime(new TimeOnly(16, 50)));
        placement.SuggestedEnd.Should().Be(Today.ToDateTime(new TimeOnly(17, 0)));
        placement.CanFullyCover.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_UserAheadOfUtc_WorkHoursCloseIsReadInLocalTime()
    {
        // UTC+2, 16:00 local (14:00 UTC), Work Hours close 17:00 local: only 1 hour is left,
        // so a 2-hour deficit can only be partly covered, 16:00–17:00 local. Mixing frames
        // used to compare 14:00 UTC with the 17:00 local close and suggest 16:00–18:00 local.
        UseUtcOffset(TimeSpan.FromHours(2));
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var utcNow = Today.ToDateTime(new TimeOnly(14, 0));
        var workHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId, workHours));
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[Deficit(presetId, utcNow.AddHours(-3), remaining: 120)]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, utcNow));
        NoConflicts();

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, utcNow), CancellationToken.None);

        var placement = result.Should().ContainSingle().Subject;
        placement.SuggestedStart.Should().Be(Today.ToDateTime(new TimeOnly(14, 0)));   // 16:00 local
        placement.SuggestedEnd.Should().Be(Today.ToDateTime(new TimeOnly(15, 0)));     // 17:00 local
        placement.CanFullyCover.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_JustAfterLocalMidnight_TodayIsTheLocalDay()
    {
        // UTC+2, 00:30 local on Today is still 22:30 UTC the day before: "today" must be the
        // user's day, not the UTC one, when fetching the day's blocks.
        UseUtcOffset(TimeSpan.FromHours(2));
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var utcNow = Today.AddDays(-1).ToDateTime(new TimeOnly(22, 30));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[Deficit(presetId, utcNow.AddHours(-3), remaining: 30)]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, utcNow));
        NoConflicts();

        await CreateHandler().HandleAsync(new GetSuggestedRedemptionPlacementsQuery(contractId, utcNow), CancellationToken.None);

        await _blocks.Received(1).GetForDayAsync(Today, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_FractionalDeficit_SuggestionRoundsUpToWholeMinute()
    {
        // A fractional deficit (e.g. from an EndedEarly session, timestamped to the
        // millisecond) must round UP to a whole minute, not size the tile to the raw
        // fractional value — a whole-minute duration converts to DateTime ticks with zero
        // rounding loss, so the created tile always meets or exceeds the real deficit
        // instead of landing a tick short of it.
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var now = Today.ToDateTime(new TimeOnly(10, 0));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        var deficit = Deficit(presetId, now, remaining: 31.896581196666666);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, now));
        NoConflicts();

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, now), CancellationToken.None);

        var placement = result.Should().ContainSingle().Subject;
        placement.TotalRemainingDeficitMinutes.Should().Be(32);
        (placement.SuggestedEnd - placement.SuggestedStart).Should().Be(TimeSpan.FromMinutes(32));
        placement.CanFullyCover.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NoRoomLeftBeforeBoundary_SuggestionDropped()
    {
        // "Now" is already past the day's boundary (Work Hours closed at 17:00, it's 18:00).
        // FindPlacement's fallback branch would otherwise hand back Start=18:00/End=17:00 —
        // an End before its own Start, which FocusBlock.Create can only reject. The handler
        // must drop the suggestion instead of returning a broken range.
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var now = Today.ToDateTime(new TimeOnly(18, 0));
        var workHours = new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId, workHours));
        var deficit = Deficit(presetId, now, remaining: 30);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, now));
        NoConflicts();

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, now), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_TwoDifferentPresets_ReturnsOneEntryPerPreset()
    {
        var contractId = Guid.NewGuid();
        var presetA = Guid.NewGuid();
        var presetB = Guid.NewGuid();
        var now = Today.ToDateTime(new TimeOnly(9, 0));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[Deficit(presetA, now, 20), Deficit(presetB, now, 15)]);
        _presets.GetByIdAsync(presetA, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetA, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, now));
        _presets.GetByIdAsync(presetB, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetB, "Reading", WebsiteFilterType.Blocklist, BlockPresetType.Focus, now));
        NoConflicts();

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, now), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().ContainSingle(p => p.PresetId == presetA && p.TotalRemainingDeficitMinutes == 20);
        result.Should().ContainSingle(p => p.PresetId == presetB && p.TotalRemainingDeficitMinutes == 15);
    }

    [Fact]
    public async Task HandleAsync_TwoDeficitsSamePreset_SumsIntoOneTotal()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var now = Today.ToDateTime(new TimeOnly(9, 0));
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[Deficit(presetId, now, 20), Deficit(presetId, now.AddHours(2), 15)]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, now));
        NoConflicts();

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, now), CancellationToken.None);

        result.Should().ContainSingle(p => p.TotalRemainingDeficitMinutes == 35);
    }
}
