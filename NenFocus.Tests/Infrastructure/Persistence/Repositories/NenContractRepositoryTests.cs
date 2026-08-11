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
        DailyFloorPolicy? dailyFloorPolicy = null,
        IReadOnlyDictionary<DateOnly, double>? plannedFocusHoursByDay = null,
        WorkHoursPolicy? workHoursPolicy = null,
        DateTime? sealedAt = null,
        Guid? contractPresetId = null,
        bool allowCancellation = false,
        StrictnessRule strictnessRule = StrictnessRule.Locked)
        => NenContract.Seal(
            id: Guid.NewGuid(),
            name: "Focused January",
            goal: new NenContractGoal("Ship the feature"),
            startDate: StartDate,
            endDate: EndDate,
            strictnessRule: strictnessRule,
            dailyFloorPolicy: dailyFloorPolicy ?? DailyFloorPolicy.None,
            changeWindow: null,
            allowCancellation: allowCancellation,
            plannedFocusHoursByDay: plannedFocusHoursByDay ?? new Dictionary<DateOnly, double>(),
            sealedAt: sealedAt ?? SealedOnStart,
            workHoursPolicy: workHoursPolicy,
            contractPresetId: contractPresetId);

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
    public async Task AddAsync_DailyFloorPolicyWithDefaultOnly_RoundTrips()
    {
        var repo = CreateRepository();
        var contract = Seal(dailyFloorPolicy: new DailyFloorPolicy(new DailyFloor(4)), plannedFocusHoursByDay: FullyPlanned(4));

        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded!.DailyFloorPolicy.Default!.Hours.Should().Be(4);
        reloaded.DailyFloorPolicy.For(StartDate)!.Hours.Should().Be(4);
    }

    [Fact]
    public async Task AddAsync_DailyFloorPolicyWithPerDayExceptions_RoundTripsIncludingRestDay()
    {
        var repo = CreateRepository();
        var restDay = StartDate.AddDays(2);
        var overrideDay = StartDate.AddDays(3);
        var policy = new DailyFloorPolicy(new DailyFloor(4), new Dictionary<DateOnly, DailyFloor?>
        {
            [restDay] = null,
            [overrideDay] = new DailyFloor(2)
        });
        var plan = FullyPlanned(4);
        plan[overrideDay] = 2;
        var contract = Seal(dailyFloorPolicy: policy, plannedFocusHoursByDay: plan);

        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded!.DailyFloorPolicy.For(restDay).Should().BeNull();
        reloaded.DailyFloorPolicy.For(overrideDay)!.Hours.Should().Be(2);
        reloaded.DailyFloorPolicy.For(StartDate)!.Hours.Should().Be(4); // falls back to Default
    }

    [Fact]
    public async Task AddAsync_NoWorkHoursPolicy_ReloadsAsNull()
    {
        var repo = CreateRepository();
        var contract = Seal();

        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded!.WorkHoursPolicy.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_WorkHoursPolicyWithPerDayException_RoundTrips()
    {
        var repo = CreateRepository();
        var exceptionDay = StartDate.AddDays(1);
        var policy = new WorkHoursPolicy(
            new WorkHours(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            new Dictionary<DateOnly, WorkHours?>
            {
                [exceptionDay] = new WorkHours(new TimeOnly(11, 0), new TimeOnly(20, 0))
            });
        var contract = Seal(workHoursPolicy: policy);

        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var reloaded = await CreateRepository().GetByIdAsync(contract.Id);
        reloaded!.WorkHoursPolicy!.For(StartDate)!.Start.Should().Be(new TimeOnly(9, 0));
        reloaded.WorkHoursPolicy.For(exceptionDay)!.Start.Should().Be(new TimeOnly(11, 0));
    }

    [Fact]
    public async Task GetDueForActivationAsync_ScheduledAndStartDateArrived_ReturnsIt()
    {
        var repo = CreateRepository();
        var contract = Seal(sealedAt: SealedBeforeStart); // Scheduled
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetDueForActivationAsync(StartDate.ToDateTime(TimeOnly.MinValue));

        result.Should().ContainSingle(c => c.Id == contract.Id);
    }

    [Fact]
    public async Task GetDueForActivationAsync_AlreadyActive_ExcludesIt()
    {
        var repo = CreateRepository();
        var contract = Seal(sealedAt: SealedOnStart); // Active
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetDueForActivationAsync(StartDate.ToDateTime(TimeOnly.MinValue));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDueForCompletionAsync_ActiveAndEndDatePassed_ReturnsIt()
    {
        var repo = CreateRepository();
        var contract = Seal(sealedAt: SealedOnStart); // Active
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetDueForCompletionAsync(EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue));

        result.Should().ContainSingle(c => c.Id == contract.Id);
    }

    [Fact]
    public async Task GetDueForCompletionAsync_OnTheEndDateItself_ExcludesIt()
    {
        var repo = CreateRepository();
        var contract = Seal(sealedAt: SealedOnStart);
        await repo.AddAsync(contract);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetDueForCompletionAsync(EndDate.ToDateTime(TimeOnly.MinValue));

        result.Should().BeEmpty();
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
            Guid.NewGuid(), "Later Contract", new NenContractGoal("Goal"),
            StartDate.AddDays(30), EndDate.AddDays(30),
            StrictnessRule.Locked, DailyFloorPolicy.None, null, false,
            new Dictionary<DateOnly, double>(), sealedAt: StartDate.AddDays(30).ToDateTime(TimeOnly.MinValue)); // Active
        await repo.AddAsync(scheduled);
        await repo.AddAsync(active);
        await Context.SaveChangesAsync();

        var result = await CreateRepository().GetAllInEffectAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(c => c.Id == scheduled.Id);
        result.Should().Contain(c => c.Id == active.Id);
    }
}
