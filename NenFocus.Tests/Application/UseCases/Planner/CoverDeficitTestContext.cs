using NenFocus.Application.Interfaces;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.UseCases.Planner;

/// <summary>
/// A contract in effect (1–9 January) that owes 60 minutes of one preset — a Committed block
/// missed three hours before now — shared by the CoverDeficit validator and handler tests.
/// </summary>
internal sealed class CoverDeficitTestContext
{
    public static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    public INenContractRepository Contracts { get; } = Substitute.For<INenContractRepository>();
    public IPlannedBlockRepository Blocks { get; } = Substitute.For<IPlannedBlockRepository>();
    public ISessionRepository Sessions { get; } = Substitute.For<ISessionRepository>();
    public ISystemClock Clock { get; } = TestClock.Create();

    public NenContract Contract { get; }
    public FocusBlock Missed { get; }
    public Guid PresetId => Missed.BlockPresetId;

    public CoverDeficitTestContext()
    {
        Clock.SetNow(Now);
        Clock.SetTimeZoneOffset(TimeSpan.Zero);

        Contract = TestContracts.Seal(
            id: Guid.NewGuid(),
            name: "Contract",
            goal: new NenContractGoal("Goal"),
            strictnessRule: StrictnessRule.Locked,
            calendar: TestCalendars.For(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 9), new DailyFloorRule.None()),
            floorRule: new DailyFloorRule.None(),
            allowCancellation: false,
            plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
            sealedAt: At.Utc(2025, 12, 31), timeZone: At.UtcZone);
        Missed = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(Now.AddHours(-3), Now.AddHours(-2)),
            membership: TestContracts.Committed(Contract.Id));

        Contracts.GetByIdAsync(Contract.Id, Arg.Any<CancellationToken>()).Returns(Contract);
        Contracts.GetAllInEffectAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<NenContract>)[Contract]);
        Blocks.GetForContractAsync(Contract.Id, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[Missed]);
        CalendarHas();
        Sessions.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Session?)null);
    }

    /// <summary>The blocks already on the calendar, whatever range is asked.</summary>
    public void CalendarHas(params PlannedBlock[] blocks) =>
        Blocks.GetForRangeAsync(Arg.Any<TimeSlot>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)blocks);
}
