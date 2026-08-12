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
    public async Task HandleAsync_SimpleContract_IsAdvancedFalseAndNoFloorFields()
    {
        var contract = NenContract.Seal(
            id: ContractId, name: "Study Week", goal: new NenContractGoal("Focus"),
            startDate: StartDate, endDate: EndDate, strictnessRule: StrictnessRule.DailyFlexibility,
            dailyFloorPolicy: DailyFloorPolicy.PerDay(new Dictionary<DateOnly, double> { [StartDate] = 2 }),
            changeWindow: null, allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double> { [StartDate] = 2 },
            sealedAt: StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(new GetNenContractForEditQuery(ContractId));

        result.Should().NotBeNull();
        result!.IsAdvanced.Should().BeFalse();
        result.FixedDailyFloorHours.Should().BeNull();
        result.RestDays.Should().BeEmpty();
        result.Name.Should().Be("Study Week");
        result.Goal.Should().Be("Focus");
    }

    [Fact]
    public async Task HandleAsync_AdvancedContract_ExposesFixedFloorRestDaysAndWorkHours()
    {
        var restDay = StartDate.AddDays(3);
        var noWorkHoursDay = StartDate.AddDays(4);
        var plannedHours = Enumerable.Range(0, EndDate.DayNumber - StartDate.DayNumber + 1)
            .Select(StartDate.AddDays)
            .Where(d => d != restDay)
            .ToDictionary(d => d, _ => 3.0);
        var contract = NenContract.Seal(
            id: ContractId, name: "Advanced", goal: new NenContractGoal("Goal"),
            startDate: StartDate, endDate: EndDate, strictnessRule: StrictnessRule.Locked,
            dailyFloorPolicy: DailyFloorPolicy.Fixed(new DailyFloor(3), [restDay]),
            changeWindow: null, allowCancellation: false,
            plannedFocusHoursByDay: plannedHours,
            sealedAt: StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            workHoursPolicy: new WorkHoursPolicy(
                new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)),
                WorkHoursPolicy.NoConstraintExceptions([noWorkHoursDay])));
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var result = await CreateHandler().HandleAsync(new GetNenContractForEditQuery(ContractId));

        result.Should().NotBeNull();
        result!.IsAdvanced.Should().BeTrue();
        result.FixedDailyFloorHours.Should().Be(3);
        result.RestDays.Should().ContainSingle().Which.Should().Be(restDay);
        result.WorkHoursStart.Should().Be(new TimeOnly(9, 0));
        result.WorkHoursEnd.Should().Be(new TimeOnly(17, 0));
        result.NoWorkHoursDays.Should().ContainSingle().Which.Should().Be(noWorkHoursDay);
    }

    [Fact]
    public async Task HandleAsync_WithBlocks_ResolvesPresetNamesAndOrdersByStart()
    {
        var contract = NenContract.Seal(
            id: ContractId, name: "Contract", goal: new NenContractGoal("Goal"),
            startDate: StartDate, endDate: EndDate, strictnessRule: StrictnessRule.DailyFlexibility,
            dailyFloorPolicy: DailyFloorPolicy.None, changeWindow: null, allowCancellation: true,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: StartDate.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);

        var focusPresetId = Guid.NewGuid();
        var backgroundPresetId = Guid.NewGuid();
        var later = FocusBlock.Create(Guid.NewGuid(), focusPresetId,
            StartDate.ToDateTime(new TimeOnly(14, 0)), StartDate.ToDateTime(new TimeOnly(16, 0)), contractId: ContractId);
        var earlier = BackgroundBlock.Create(Guid.NewGuid(), backgroundPresetId,
            StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(10, 0)), contractId: ContractId);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[later, earlier]);

        _presets.GetByIdAsync(focusPresetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(focusPresetId, "Deep Work", WebsiteFilterType.Blocklist, BlockPresetType.Focus, StartDate.ToDateTime(TimeOnly.MinValue)));
        _presets.GetByIdAsync(backgroundPresetId, Arg.Any<CancellationToken>())
            .Returns(BlockPreset.Create(backgroundPresetId, "Ambient Block", WebsiteFilterType.Blocklist, BlockPresetType.Background, StartDate.ToDateTime(TimeOnly.MinValue)));

        var result = await CreateHandler().HandleAsync(new GetNenContractForEditQuery(ContractId));

        result!.Blocks.Should().HaveCount(2);
        result.Blocks[0].PresetName.Should().Be("Ambient Block");
        result.Blocks[0].IsFocus.Should().BeFalse();
        result.Blocks[1].PresetName.Should().Be("Deep Work");
        result.Blocks[1].IsFocus.Should().BeTrue();
    }
}
