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
    private readonly ISystemClock _clock = TestClock.Create();

    // Local time = UTC (no offset) for every test below.
    public GetSuggestedRedemptionPlacementsQueryHandlerTests() => _clock.SetTimeZoneOffset(TimeSpan.Zero);

    private GetSuggestedRedemptionPlacementsQueryHandler CreateHandler() => new(_contracts, _deficits, _presets, _clock);

    private static NenContract SameDayContract(Guid id, WorkHours? workHours = null) =>
        TestContracts.Seal(
            id: id,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(Today.AddDays(-1), Today.AddDays(7), new DailyFloorRule.None(), workHours: workHours),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Today.AddDays(-1).ToDateTime(TimeOnly.MinValue)), timeZone: At.UtcZone,
            redemptionPlacementPolicy: RedemptionPlacementPolicy.CurrentDayOnly);

    private static FocusBlockDeficitDto Deficit(Guid presetId, DateTime scheduledStart, long remaining) =>
        new()
        {
            FocusBlockId = Guid.NewGuid(),
            BlockPresetId = presetId,
            Scheduled = At.Slot(scheduledStart, scheduledStart.AddHours(1)),
            Outcome = FocusBlockOutcome.Missed,
            RemainingDeficit = WholeMinutes.Of(remaining)
        };

    [Fact]
    public async Task HandleAsync_ContractNotFound_ReturnsEmpty()
    {
        _contracts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(Guid.NewGuid(), At.Utc(Today.ToDateTime(new TimeOnly(10, 0)))), CancellationToken.None);

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
            new GetSuggestedRedemptionPlacementsQuery(contractId, At.Utc(Today.ToDateTime(new TimeOnly(10, 0)))), CancellationToken.None);

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
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Today.ToDateTime(TimeOnly.MinValue))));

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, At.Utc(Today.ToDateTime(new TimeOnly(10, 0)))), CancellationToken.None);

        result.Should().ContainSingle(p => p.PresetId == presetId && p.TotalRemainingDeficit == WholeMinutes.Of(30));
    }

    [Fact]
    public async Task HandleAsync_AnywhereInContractPolicy_DeficitMissedYesterday_CoverableToday()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var contract = TestContracts.Seal(
            id: contractId,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(Today.AddDays(-1), Today.AddDays(7), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(Today.AddDays(-1).ToDateTime(TimeOnly.MinValue)), timeZone: At.UtcZone,
            redemptionPlacementPolicy: RedemptionPlacementPolicy.AnywhereInContract);
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(contract);
        var deficit = Deficit(presetId, Today.AddDays(-1).ToDateTime(new TimeOnly(10, 0)), remaining: 30);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(Today.ToDateTime(TimeOnly.MinValue))));

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, At.Utc(Today.ToDateTime(new TimeOnly(10, 0)))), CancellationToken.None);

        result.Should().ContainSingle(p => p.PresetId == presetId && p.TotalRemainingDeficit == WholeMinutes.Of(30));
    }

    [Fact]
    public async Task HandleAsync_SnapsToNextQuarterHour()
    {
        var contractId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var now = Today.ToDateTime(new TimeOnly(10, 7)); // 7 min past the hour
        _contracts.GetByIdAsync(contractId, Arg.Any<CancellationToken>()).Returns(SameDayContract(contractId));
        var deficit = Deficit(presetId, now, remaining: 40);
        _deficits.HandleAsync(new GetNenContractDeficitsQuery(contractId), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<FocusBlockDeficitDto>)[deficit]);
        _presets.GetByIdAsync(presetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(now)));

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, At.Utc(now)), CancellationToken.None);

        var placement = result.Should().ContainSingle().Subject;
        placement.PresetId.Should().Be(presetId);
        placement.PresetName.Should().Be("Deep Work");
        placement.TotalRemainingDeficit.Should().Be(WholeMinutes.Of(40));
        placement.Suggested.Should().Be(At.Slot(Today.ToDateTime(new TimeOnly(10, 15)), Today.ToDateTime(new TimeOnly(10, 55))));
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
            .Returns(BlockPreset.Create(presetA, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(now)));
        _presets.GetByIdAsync(presetB, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(presetB, "Reading", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(now)));

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, At.Utc(now)), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().ContainSingle(p => p.PresetId == presetA && p.TotalRemainingDeficit == WholeMinutes.Of(20));
        result.Should().ContainSingle(p => p.PresetId == presetB && p.TotalRemainingDeficit == WholeMinutes.Of(15));
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
            .Returns(BlockPreset.Create(presetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(now)));

        var result = await CreateHandler().HandleAsync(
            new GetSuggestedRedemptionPlacementsQuery(contractId, At.Utc(now)), CancellationToken.None);

        result.Should().ContainSingle(p => p.TotalRemainingDeficit == WholeMinutes.Of(35));
    }
}
