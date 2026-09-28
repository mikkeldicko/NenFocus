using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.UseCases.NenContracts.SweepNenContractLifecycle;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence.Repositories;
using NSubstitute;

namespace NenFocus.Tests.Infrastructure.Persistence;

/// <summary>
/// The lifecycle sweep end to end against real SQLite: a contract sealed for tomorrow with
/// Work Hours, reloaded from the database (days deserialized), activated at its start moment
/// and persisted. Reproduces the Gap 65 manual test "turns Active at 09:00".
/// </summary>
public class ContractLifecycleSweepPersistenceTests : SqliteRepositoryTestBase
{
    private static readonly DateOnly Tomorrow = new(2026, 9, 29);

    private static NenContract ScheduledForTomorrowNineToSix() => NenContract.Seal(
        id: Guid.NewGuid(),
        name: "Test contract",
        goal: new NenContractGoal("Goal"),
        strictnessRule: StrictnessRule.DailyFlexibility,
        calendar: TestCalendars.For(Tomorrow, Tomorrow.AddDays(6), new DailyFloorRule.None(),
            workHours: new WorkHours(new TimeOnly(9, 0), new TimeOnly(18, 0))),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: true,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        localSealedAt: Tomorrow.AddDays(-1).ToDateTime(new TimeOnly(16, 38)));

    private async Task<NenContractStatus> SweepAtAsync(DateTime localNow)
    {
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(localNow);
        clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>());

        await using var sweepContext = CreateFreshContext();
        var handler = new SweepNenContractLifecycleCommandHandler(new NenContractRepository(sweepContext), clock);
        await handler.HandleAsync(new SweepNenContractLifecycleCommand(), CancellationToken.None);
        await sweepContext.SaveChangesAsync();

        await using var readContext = CreateFreshContext();
        return (await new NenContractRepository(readContext).GetAllAsync()).Single().Status;
    }

    [Fact]
    public async Task Sweep_BeforeTheStartMoment_LeavesItScheduled()
    {
        await new NenContractRepository(Context).AddAsync(ScheduledForTomorrowNineToSix());
        await Context.SaveChangesAsync();

        (await SweepAtAsync(Tomorrow.ToDateTime(new TimeOnly(8, 59)))).Should().Be(NenContractStatus.Scheduled);
    }

    [Fact]
    public async Task Sweep_AtTheStartMoment_ActivatesAndPersists()
    {
        await new NenContractRepository(Context).AddAsync(ScheduledForTomorrowNineToSix());
        await Context.SaveChangesAsync();

        (await SweepAtAsync(Tomorrow.ToDateTime(new TimeOnly(9, 0, 30)))).Should().Be(NenContractStatus.Active);
    }
}
