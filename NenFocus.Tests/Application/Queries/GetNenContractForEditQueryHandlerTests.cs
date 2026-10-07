using NenFocus.Application.DTOs;
using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractForEdit;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetNenContractForEditQueryHandlerTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly DateOnly StartDate = new(2026, 6, 1);
    private static readonly DateOnly EndDate = new(2026, 6, 7);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockPresetRepository _presets = Substitute.For<IBlockPresetRepository>();

    public GetNenContractForEditQueryHandlerTests()
    {
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);
    }

    private GetNenContractForEditQueryHandler CreateHandler() => new(_contracts, _blocks, _presets);

    [Fact]
    public async Task HandleAsync_ContractNotFound_ReturnsNull()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(new GetNenContractForEditQuery(ContractId));

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_FollowsPlanContract_NoFixedFloor_DaysCarryPlannedFloors()
    {
        var contract = TestContracts.Seal(
            id: ContractId,
            name: "Study Week",
            goal: new NenContractGoal("Focus"),
            strictnessRule: StrictnessRule.DailyFlexibility,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.FollowsPlan(), plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = 2 }),
            floorRule: new DailyFloorRule.FollowsPlan(),
            allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = 2 },
            sealedAt: At.Utc(StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(new GetNenContractForEditQuery(ContractId));

        result.Should().NotBeNull();
        result!.FixedDailyFloorHours.Should().BeNull();
        result.Days.Should().HaveCount(EndDate.DayNumber - StartDate.DayNumber + 1);
        result.Days[0].FloorHours.Should().Be(2);
        result.Days[1].FloorHours.Should().BeNull();
        result.Goal.Should().Be("Focus");
    }

    [Fact]
    public async Task HandleAsync_FixedFloorContract_ExposesTheRuleAndEachDaysWindowAndFloor()
    {
        var restDay = StartDate.AddDays(3);
        var noWorkHoursDay = StartDate.AddDays(4);
        var plannedHours = Enumerable.Range(0, EndDate.DayNumber - StartDate.DayNumber + 1)
            .Select(StartDate.AddDays)
            .Where(d => d != restDay)
            .ToDictionary(d => d, _ => 3.0);
        var contract = TestContracts.Seal(
            id: ContractId, name: "Advanced", goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.Fixed(new DailyFloor(3)),
                workHours: new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)),
                restDays: [restDay], noWorkHoursDays: [noWorkHoursDay]),
            floorRule: new DailyFloorRule.Fixed(new DailyFloor(3)),
            allowCancellation: false,
            plannedFocusHoursByDay: plannedHours,
            sealedAt: At.Utc(StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(new GetNenContractForEditQuery(ContractId));

        result.Should().NotBeNull();
        result!.FixedDailyFloorHours.Should().Be(3);
        result.Days.Single(d => d.Date == restDay).FloorHours.Should().BeNull();
        result.Days.Single(d => d.Date == StartDate).Should().Be(new NenContractEditDayDto(
            StartDate, StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(17, 0)), 3));
        result.Days.Single(d => d.Date == noWorkHoursDay).Should().Be(new NenContractEditDayDto(
            noWorkHoursDay, noWorkHoursDay.ToDateTime(TimeOnly.MinValue), noWorkHoursDay.AddDays(1).ToDateTime(TimeOnly.MinValue), 3));
    }

    [Fact]
    public async Task HandleAsync_WithBlocks_ResolvesPresetNamesAndOrdersByStart()
    {
        var contract = TestContracts.Seal(
            id: ContractId,
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.DailyFlexibility,
            calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), timeZone: At.UtcZone);
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var focusPresetId = Guid.NewGuid();
        var backgroundPresetId = Guid.NewGuid();
        var later = FocusBlock.Create(Guid.NewGuid(), focusPresetId,At.Slot(StartDate.ToDateTime(new TimeOnly(14, 0)), StartDate.ToDateTime(new TimeOnly(16, 0))), membership: TestContracts.Committed(ContractId));
        var earlier = BackgroundBlock.Create(Guid.NewGuid(), backgroundPresetId,At.Slot(StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0))), membership: TestContracts.Committed(ContractId));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[later, earlier]);

        _presets.GetByIdAsync(focusPresetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(focusPresetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, At.Utc(StartDate.ToDateTime(TimeOnly.MinValue))));
        _presets.GetByIdAsync(backgroundPresetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(backgroundPresetId, "Ambient Block", WebsiteFilterType.Blocklist, BlockPresetType.Background, At.Utc(StartDate.ToDateTime(TimeOnly.MinValue))));

        var result = await CreateHandler().HandleAsync(new GetNenContractForEditQuery(ContractId));

        result!.Blocks.Should().HaveCount(2);
        result.Blocks[0].PresetName.Should().Be("Ambient Block");
        result.Blocks[0].IsFocus.Should().BeFalse();
        result.Blocks[1].PresetName.Should().Be("Deep Work");
        result.Blocks[1].IsFocus.Should().BeTrue();
    }
}
