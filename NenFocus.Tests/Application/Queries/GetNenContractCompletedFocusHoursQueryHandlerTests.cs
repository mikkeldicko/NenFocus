using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.GetNenContractCompletedFocusHours;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class GetNenContractCompletedFocusHoursQueryHandlerTests
{
    private static readonly Guid ContractId = Guid.NewGuid();
    private static readonly DateOnly StartDate = new(2026, 1, 1);
    private static readonly DateOnly EndDate = new(2026, 1, 7);
    private static readonly DateTime Now = new(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly INenContractRepository _contracts = Substitute.For<INenContractRepository>();
    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IFocusBlockOutcomeClassifier _classifier = Substitute.For<IFocusBlockOutcomeClassifier>();
    // Real, pure implementation — already covered by its own tests (incl. cross-midnight
    // splitting); using it here keeps this handler's tests focused on which windows it
    // hands over, not re-verifying summing/splitting math a second time.
    private readonly IPlannedFocusHoursCalculator _calculator = new PlannedFocusHoursCalculator();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();

    public GetNenContractCompletedFocusHoursQueryHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>());
        _sessions.GetByOriginatingBlockIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Session?)null);
        _sessions.GetHistoryAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[]);
    }

    private GetNenContractCompletedFocusHoursQueryHandler CreateHandler() => new(_contracts, _blocks, _sessions, _classifier, _calculator, _clock);

    private static NenContract ActiveContract() => NenContract.Seal(
        id: ContractId,
        name: "Focused Week",
        goal: new NenContractGoal("Ship it"),
        strictnessRule: StrictnessRule.Locked,
        calendar: TestCalendars.For(StartDate, EndDate, new DailyFloorRule.None()),
        floorRule: new DailyFloorRule.None(),
        changeWindow: null,
        allowCancellation: false,
        plannedFocusHoursByDay: new Dictionary<DateOnly, double>(),
        localSealedAt: StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    [Fact]
    public async Task HandleAsync_ContractNotFound_ReturnsEmpty()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns((NenContract?)null);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_OpenBlock_NotCredited()
    {
        // CreditedWindow deliberately left unstubbed — an open/unresolved block credits
        // nothing, and NSubstitute's default null for an unconfigured nullable return
        // already models that correctly.
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-1), Now.AddHours(1), contractId: ContractId);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_CompletedOnTimeBlock_CreditsFullDurationViaGracePeriod()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var day = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), day, day.AddHours(2), contractId: ContractId);
        block.MarkRitualCompleted(day.AddMinutes(3));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.CreditedWindow(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), 0)
            .Returns(((DateTime, DateTime)?)(block.Start, block.End)); // grace-period credit: full window

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result[DateOnly.FromDateTime(day)].Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_MissedBlock_CreditsZero()
    {
        // CreditedWindow left unstubbed — Missed credits nothing.
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var day = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), day, day.AddHours(2), contractId: ContractId);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().NotContainKey(DateOnly.FromDateTime(day));
    }

    [Fact]
    public async Task HandleAsync_EndedEarlyBlock_CreditsOnlyActualElapsedMinutes()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var day = new DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), day, day.AddHours(2), contractId: ContractId);
        block.MarkRitualCompleted(day); // started right on time
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);

        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(120)),
            block.BlockPresetId, SessionOrigin.Planned, originatingBlockId: block.Id);
        session.Start(day);
        session.Complete(day.AddMinutes(20)); // quit after 20 of the 120 scheduled minutes
        _sessions.GetByOriginatingBlockIdAsync(block.Id, Arg.Any<CancellationToken>()).Returns(session);

        _classifier.CreditedWindow(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), 20)
            .Returns(((DateTime, DateTime)?)(day, day.AddMinutes(20))); // no grace-period credit for an early end

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result[DateOnly.FromDateTime(day)].Should().BeApproximately(20.0 / 60.0, 0.001); // ~20 minutes, not the full 2 hours
    }

    [Fact]
    public async Task HandleAsync_CrossMidnightBlock_SplitsCreditedHoursAcrossBothDays()
    {
        // The credited window itself spans midnight (a 22:00-01:00 overnight block, fully
        // credited via the grace period) — proves the handler's local-converted window
        // actually gets split per day, not attributed wholesale to the start day.
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var start = new DateTime(2026, 1, 2, 22, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 3, 1, 0, 0, DateTimeKind.Utc); // 22:00-01:00, 3h
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), start, end, contractId: ContractId);
        block.MarkRitualCompleted(start.AddMinutes(2));
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.CreditedWindow(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), 0)
            .Returns(((DateTime, DateTime)?)(start, end));

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result[new DateOnly(2026, 1, 2)].Should().Be(2); // 22:00-24:00
        result[new DateOnly(2026, 1, 3)].Should().Be(1); // 00:00-01:00
    }

    [Fact]
    public async Task HandleAsync_SpontaneousSessionMatchingOnlyABackgroundBlockPreset_Excluded()
    {
        // A Spontaneous session can never actually carry a Background preset's ID in
        // practice (Ren only offers Focus presets), but the query must not rely on that
        // fact by accident — it should only ever match against Focus presets.
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var backgroundPresetId = Guid.NewGuid();
        var background = BackgroundBlock.Create(Guid.NewGuid(), backgroundPresetId,
            Now.AddDays(-1), Now.AddDays(-1).AddHours(1), contractId: ContractId);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[background]);

        var spontaneousStart = new DateTime(2026, 1, 3, 10, 0, 0, DateTimeKind.Utc);
        var spontaneous = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(45)),
            backgroundPresetId, SessionOrigin.Spontaneous); // deliberately uses the Background preset's ID
        spontaneous.Start(spontaneousStart);
        spontaneous.Complete(spontaneousStart.AddMinutes(45));
        _sessions.GetHistoryAsync(StartDate.AddDays(-1), EndDate.AddDays(1), null, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[spontaneous]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_MultipleFocusBlocksSameDay_HoursSum()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var day = new DateOnly(2026, 1, 2);
        var block1 = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 1, 2, 9, 0, 0), new DateTime(2026, 1, 2, 10, 0, 0), contractId: ContractId);
        var block2 = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 1, 2, 14, 0, 0), new DateTime(2026, 1, 2, 15, 30, 0), contractId: ContractId);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block1, block2]);
        _classifier.CreditedWindow(block1.Start, block1.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), 0)
            .Returns(((DateTime, DateTime)?)(block1.Start, block1.End));
        _classifier.CreditedWindow(block2.Start, block2.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), 0)
            .Returns(((DateTime, DateTime)?)(block2.Start, block2.End));

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result[day].Should().Be(2.5); // 60 + 90 = 150 minutes = 2.5 hours
    }

    [Fact]
    public async Task HandleAsync_MatchingSpontaneousSession_Counts()
    {
        var contract = ActiveContract();
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, Now.AddDays(-1), Now.AddDays(-1).AddHours(1), contractId: ContractId);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);

        var spontaneousStart = new DateTime(2026, 1, 3, 10, 0, 0, DateTimeKind.Utc);
        var spontaneous = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(45)),
            presetId, SessionOrigin.Spontaneous);
        spontaneous.Start(spontaneousStart);
        spontaneous.Complete(spontaneousStart.AddMinutes(45));
        _sessions.GetHistoryAsync(StartDate.AddDays(-1), EndDate.AddDays(1), null, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[spontaneous]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result[DateOnly.FromDateTime(spontaneousStart)].Should().Be(0.75); // 45 minutes
    }

    [Fact]
    public async Task HandleAsync_SpontaneousSessions_MatchedOnTheContractsLocalDays()
    {
        // At UTC+2, 01:00 local on the first day is 23:00 UTC the day before, and 01:00 local
        // the day after the last day is 23:00 UTC on the last day. The contract's dates are
        // local, so the first counts and the second doesn't — whatever UTC day they fall on.
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>().AddHours(2));
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, Now.AddDays(-1), Now.AddDays(-1).AddHours(1), contractId: ContractId);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);

        var firstDayLocal = Spontaneous(presetId, new DateTime(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc));
        var dayAfterEndLocal = Spontaneous(presetId, new DateTime(2026, 1, 7, 23, 0, 0, DateTimeKind.Utc));
        _sessions.GetHistoryAsync(StartDate.AddDays(-1), EndDate.AddDays(1), null, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[firstDayLocal, dayAfterEndLocal]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result[StartDate].Should().Be(0.5);
        result.Should().NotContainKey(EndDate.AddDays(1));
    }

    private static Session Spontaneous(Guid presetId, DateTime startUtc)
    {
        var session = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            presetId, SessionOrigin.Spontaneous);
        session.Start(startUtc);
        session.Complete(startUtc.AddMinutes(30));
        return session;
    }

    [Fact]
    public async Task HandleAsync_CrossMidnightSpontaneousSession_SplitsAcrossBothDays()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var presetId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), presetId, Now.AddDays(-1), Now.AddDays(-1).AddHours(1), contractId: ContractId);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);

        var spontaneousStart = new DateTime(2026, 1, 3, 23, 0, 0, DateTimeKind.Utc);
        var spontaneous = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(120)),
            presetId, SessionOrigin.Spontaneous);
        spontaneous.Start(spontaneousStart);
        spontaneous.Complete(spontaneousStart.AddHours(2)); // 23:00-01:00
        _sessions.GetHistoryAsync(StartDate.AddDays(-1), EndDate.AddDays(1), null, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[spontaneous]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result[new DateOnly(2026, 1, 3)].Should().Be(1);
        result[new DateOnly(2026, 1, 4)].Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_SpontaneousSessionWithNonMatchingPreset_Excluded()
    {
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);

        var spontaneousStart = new DateTime(2026, 1, 3, 10, 0, 0, DateTimeKind.Utc);
        var spontaneous = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(45)),
            Guid.NewGuid(), SessionOrigin.Spontaneous); // preset unrelated to the contract
        spontaneous.Start(spontaneousStart);
        spontaneous.Complete(spontaneousStart.AddMinutes(45));
        _sessions.GetHistoryAsync(StartDate.AddDays(-1), EndDate.AddDays(1), null, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[spontaneous]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_PlannedSpontaneousSessionMixedOrigin_OnlySpontaneousCounted()
    {
        var contract = ActiveContract();
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(contract);
        var presetId = Guid.NewGuid();
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[]);

        var plannedLikeStart = new DateTime(2026, 1, 3, 10, 0, 0, DateTimeKind.Utc);
        var plannedOriginSession = Session.Create(Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            presetId, SessionOrigin.Planned, originatingBlockId: Guid.NewGuid());
        plannedOriginSession.Start(plannedLikeStart);
        plannedOriginSession.Complete(plannedLikeStart.AddMinutes(30));

        _sessions.GetHistoryAsync(StartDate.AddDays(-1), EndDate.AddDays(1), null, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Session>)[plannedOriginSession]);

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        // A Planned-origin session in history isn't this query's concern (its FocusBlock,
        // if under the contract, is already credited via the block loop) — excluded here
        // since it's not Spontaneous.
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ConvertsCreditedWindowsToLocalTime_BeforeSplittingByDay()
    {
        // 1:00 AM UTC Jan 3 — but 8:00 PM local (UTC-5) on Jan 2. A fix that forgot to
        // convert would credit Jan 3 instead of Jan 2.
        _contracts.GetByIdAsync(ContractId, Arg.Any<CancellationToken>()).Returns(ActiveContract());
        var start = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 3, 1, 0, 0, DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), start, end, contractId: ContractId);
        block.MarkRitualCompleted(start);
        _blocks.GetForContractAsync(ContractId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PlannedBlock>)[block]);
        _classifier.CreditedWindow(block.Start, block.End, Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), 0)
            .Returns(((DateTime, DateTime)?)(start, end));
        _clock.ToLocal(Arg.Any<DateTime>()).Returns(ci => ci.Arg<DateTime>().AddHours(-5));

        var result = await CreateHandler().HandleAsync(new GetNenContractCompletedFocusHoursQuery(ContractId), CancellationToken.None);

        result.Should().ContainKey(new DateOnly(2026, 1, 2));
        result.Should().NotContainKey(new DateOnly(2026, 1, 3));
    }
}
