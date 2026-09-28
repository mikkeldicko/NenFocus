using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence.Repositories;

namespace NenFocus.Tests.Infrastructure.Persistence.Repositories;

public class NenContractRepositoryTests : SqliteRepositoryTestBase
{
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);
    private static readonly DateTime SealedOnStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SealedBeforeStart = new(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    private NenContractRepository CreateRepository() => new(Context);

    private static NenContract Seal(
        DailyFloorRule? floorRule = null,
        IReadOnlyDictionary<DateOnly, double>? plannedFocusHoursByDay = null,
        WorkHours? workHours = null,
        DateTime? sealedAt = null,
        Guid? contractPresetId = null,
        bool allowCancellation = false,
        StrictnessRule strictnessRule = StrictnessRule.Locked,
        IReadOnlyCollection<DateOnly>? restDays = null,
        IReadOnlyCollection<DateOnly>? noWorkHoursDays = null)
    {
        var rule = floorRule ?? new DailyFloorRule.None();
        var plan = plannedFocusHoursByDay ?? new Dictionary<DateOnly, double>();
        return NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Focused January",
            goal: new NenContractGoal("Ship the feature"),
            strictnessRule: strictnessRule,
            calendar: TestCalendars.For(StartDate, EndDate, rule, plan, workHours, restDays, noWorkHoursDays),
            floorRule: rule,
            changeWindow: null,
            allowCancellation: allowCancellation,
            plannedFocusHoursByDay: plan,
            localSealedAt: sealedAt ?? SealedOnStart,
            contractPresetId: contractPresetId);
    }

    private static Dictionary<DateOnly, double> FullyPlanned(double hoursPerDay)
    {
        var map = new Dictionary<DateOnly, double>();
        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
            map[date] = hoursPerDay;
        return map;
    }

    [Fact]
    public async Task AddAsync_ThenReload_RoundTripsScalarFields()
    {
        var repo = CreateRepository();
        var contract = Seal(strictnessRule: StrictnessRule.PeriodTimeBudget, allowCancellation: true);

        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Name.Should().Be("Focused January");
        reloaded.Goal.Value.Should().Be("Ship the feature");
        reloaded.StartDate.Should().Be(StartDate);
        reloaded.EndDate.Should().Be(EndDate);
        reloaded.StrictnessRule.Should().Be(StrictnessRule.PeriodTimeBudget);
        reloaded.AllowCancellation.Should().BeTrue();
        reloaded.Status.Should().Be(NenContractStatus.Active);
    }

    [Fact]
    public async Task AddAsync_WithContractPresetId_RoundTrips()
    {
        var repo = CreateRepository();
        var presetId = Guid.NewGuid();
        var contract = Seal(contractPresetId: presetId);

        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded!.ContractPresetId.Should().Be(presetId);
    }

    [Fact]
    public async Task AddAsync_NoContractPresetId_ReloadsAsNull()
    {
        var repo = CreateRepository();
        var contract = Seal();

        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded!.ContractPresetId.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_FixedFloorWithRestDay_RoundTripsDaysAndRule()
    {
        var repo = CreateRepository();
        var restDay = StartDate.AddDays(2);
        var plan = FullyPlanned(4);
        plan.Remove(restDay);
        var contract = Seal(floorRule: new DailyFloorRule.Fixed(new DailyFloor(4)), restDays: [restDay], plannedFocusHoursByDay: plan);

        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded!.FloorRule.Should().Be(new DailyFloorRule.Fixed(new DailyFloor(4)));
        reloaded.FloorHoursFor(StartDate).Should().Be(4);
        reloaded.IsRestDay(restDay).Should().BeTrue();
    }

    [Fact]
    public async Task AddAsync_FollowsPlanFloors_RoundTripPerDay()
    {
        var repo = CreateRepository();
        var plan = new Dictionary<DateOnly, double> { [StartDate] = 2, [StartDate.AddDays(1)] = 3.5 };
        var contract = Seal(floorRule: new DailyFloorRule.FollowsPlan(), plannedFocusHoursByDay: plan);

        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded!.FloorRule.Should().Be(new DailyFloorRule.FollowsPlan());
        reloaded.FloorHoursFor(StartDate).Should().Be(2);
        reloaded.FloorHoursFor(StartDate.AddDays(1)).Should().Be(3.5);
        reloaded.IsRestDay(StartDate.AddDays(2)).Should().BeTrue();
    }

    [Fact]
    public async Task AddAsync_NoWorkHours_ReloadsFullDayWindowsAndMidnightEnd()
    {
        var repo = CreateRepository();
        var contract = Seal();

        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded!.StartsAt.Should().Be(StartDate.ToDateTime(TimeOnly.MinValue));
        reloaded.EndsAt.Should().Be(EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue)); // 24:00 survives the round trip
    }

    [Fact]
    public async Task AddAsync_WorkHoursWithNoWorkHoursDay_RoundTrips()
    {
        var repo = CreateRepository();
        var fullDay = StartDate.AddDays(1);
        var contract = Seal(
            workHours: new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            noWorkHoursDays: [fullDay],
            sealedAt: SealedBeforeStart);

        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded!.WindowOn(StartDate).Should().Be((StartDate.ToDateTime(new TimeOnly(9, 0)), StartDate.ToDateTime(new TimeOnly(17, 0))));
        reloaded.WindowOn(fullDay).Should().Be((fullDay.ToDateTime(TimeOnly.MinValue), fullDay.AddDays(1).ToDateTime(TimeOnly.MinValue)));
    }

    [Fact]
    public async Task SaveAfterBreak_PersistsTheShortenedDays()
    {
        var repo = CreateRepository();
        var contract = Seal(allowCancellation: true);
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        contract.Break(StartDate.AddDays(2).ToDateTime(new TimeOnly(10, 0)));
        await repo.SaveAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded!.EndDate.Should().Be(StartDate.AddDays(2));
        reloaded.WindowOn(StartDate.AddDays(3)).Should().BeNull();
    }

    [Fact]
    public async Task HasContractsInEffectForPresetAsync_NoContractsForPreset_ReturnsFalse()
    {
        var result = await CreateRepository().HasContractsInEffectForPresetAsync(Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasContractsInEffectForPresetAsync_ActiveContractForPreset_ReturnsTrue()
    {
        var repo = CreateRepository();
        var presetId = Guid.NewGuid();
        var contract = Seal(contractPresetId: presetId, sealedAt: SealedOnStart); // Active
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().HasContractsInEffectForPresetAsync(presetId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasContractsInEffectForPresetAsync_ScheduledContractForPreset_ReturnsTrue()
    {
        var repo = CreateRepository();
        var presetId = Guid.NewGuid();
        var contract = Seal(contractPresetId: presetId, sealedAt: SealedBeforeStart); // Scheduled
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().HasContractsInEffectForPresetAsync(presetId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasContractsInEffectForPresetAsync_CompletedContractForPreset_ReturnsFalse()
    {
        var repo = CreateRepository();
        var presetId = Guid.NewGuid();
        var contract = Seal(contractPresetId: presetId, sealedAt: SealedOnStart, allowCancellation: true);
        contract.Complete(SealedOnStart.AddDays(7));
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().HasContractsInEffectForPresetAsync(presetId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_UntrackedContract_AddsIt()
    {
        var contract = Seal();

        await CreateRepository().SaveAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveAsync_AlreadyTrackedContract_PersistsMutation()
    {
        var repo = CreateRepository();
        var contract = Seal(sealedAt: SealedBeforeStart, allowCancellation: true); // Scheduled
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var tracked = await repo.GetByIdAsync(contract.Id);
        tracked!.Activate(SealedOnStart);
        await repo.SaveAsync(tracked);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded!.Status.Should().Be(NenContractStatus.Active);
    }

    [Fact]
    public async Task GetAllInEffectAsync_NoContracts_ReturnsEmpty()
    {
        var result = await CreateRepository().GetAllInEffectAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllInEffectAsync_ActiveContract_IncludesIt()
    {
        var repo = CreateRepository();
        var contract = Seal(sealedAt: SealedOnStart); // Active
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetAllInEffectAsync();

        result.Should().ContainSingle(c => c.Id == contract.Id);
    }

    [Fact]
    public async Task GetAllInEffectAsync_ScheduledContract_IncludesIt()
    {
        var repo = CreateRepository();
        var contract = Seal(sealedAt: SealedBeforeStart); // Scheduled
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetAllInEffectAsync();

        result.Should().ContainSingle(c => c.Id == contract.Id);
    }

    [Fact]
    public async Task GetAllInEffectAsync_CompletedContract_ExcludesIt()
    {
        var repo = CreateRepository();
        var contract = Seal(sealedAt: SealedOnStart);
        contract.Complete(SealedOnStart.AddDays(7));
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetAllInEffectAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllInEffectAsync_ActiveAndScheduledBothPresent_ReturnsBoth()
    {
        var repo = CreateRepository();
        var scheduled = Seal(sealedAt: SealedBeforeStart); // Scheduled
        var active = NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Later Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(StartDate.AddDays(30), EndDate.AddDays(30), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            localSealedAt: StartDate.AddDays(30).ToDateTime(TimeOnly.MinValue)); // Active
        await repo.AddAsync(scheduled);
        await repo.AddAsync(active);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetAllInEffectAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(c => c.Id == scheduled.Id);
        result.Should().Contain(c => c.Id == active.Id);
    }

    [Fact]
    public async Task GetAllAsync_NoContracts_ReturnsEmpty()
    {
        var result = await CreateRepository().GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ScheduledContract_IncludesIt()
    {
        var repo = CreateRepository();
        var contract = Seal(sealedAt: SealedBeforeStart); // Scheduled
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetAllAsync();

        result.Should().ContainSingle(c => c.Id == contract.Id);
    }

    [Fact]
    public async Task GetAllAsync_CompletedContract_IncludesIt()
    {
        var repo = CreateRepository();
        var contract = Seal(sealedAt: SealedOnStart);
        contract.Complete(SealedOnStart.AddDays(7));
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetAllAsync();

        result.Should().ContainSingle(c => c.Id == contract.Id);
    }

    [Fact]
    public async Task GetAllAsync_BrokenContract_IncludesIt()
    {
        var repo = CreateRepository();
        var contract = Seal(sealedAt: SealedOnStart, allowCancellation: true);
        contract.Break(SealedOnStart.AddDays(2));
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetAllAsync();

        result.Should().ContainSingle(c => c.Id == contract.Id);
    }

    [Fact]
    public async Task GetAllAsync_MixOfStatuses_ReturnsAllOfThem()
    {
        var repo = CreateRepository();
        var scheduled = Seal(sealedAt: SealedBeforeStart); // Scheduled
        var active = NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Later Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(StartDate.AddDays(30), EndDate.AddDays(30), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            changeWindow: null,
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            localSealedAt: StartDate.AddDays(30).ToDateTime(TimeOnly.MinValue)); // Active
        var completed = Seal(sealedAt: SealedOnStart);
        completed.Complete(SealedOnStart.AddDays(7));
        await repo.AddAsync(scheduled);
        await repo.AddAsync(active);
        await repo.AddAsync(completed);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetAllAsync();

        result.Should().HaveCount(3);
        result.Should().Contain(c => c.Id == scheduled.Id);
        result.Should().Contain(c => c.Id == active.Id);
        result.Should().Contain(c => c.Id == completed.Id);
    }
}
