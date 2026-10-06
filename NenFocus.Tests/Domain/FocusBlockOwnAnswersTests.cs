using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

/// <summary>
/// The block answers for itself from its own stamps (Gap 95): late start, outcome, the span it
/// was worked over and the credit it earned. Its stamps are the only source those answers read,
/// so it refuses a session end its rules can't interpret.
/// </summary>
public class FocusBlockOwnAnswersTests
{
    private static readonly DateTime ScheduledStart = new(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ScheduledEnd = new(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc);

    private static FocusBlock NewBlock() =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(ScheduledStart, ScheduledEnd));

    // --- MarkSessionEnded guards (decision 3) ---

    [Fact]
    public void MarkSessionEnded_WithoutRitual_Throws()
    {
        var block = NewBlock();

        var act = () => block.MarkSessionEnded(At.Utc(ScheduledStart.AddHours(1)));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkSessionEnded_BeforeRitual_Throws()
    {
        var block = NewBlock();
        block.MarkRitualCompleted(At.Utc(ScheduledStart.AddMinutes(30)));

        var act = () => block.MarkSessionEnded(At.Utc(ScheduledStart.AddMinutes(29)));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkSessionEnded_AtRitualMoment_IsAccepted()
    {
        var block = NewBlock();
        var ritualAt = At.Utc(ScheduledStart.AddMinutes(30));
        block.MarkRitualCompleted(ritualAt);

        block.MarkSessionEnded(ritualAt);

        block.SessionEndedAt.Should().Be(ritualAt);
    }

    // --- IsFinal and OwnDeficit (decisions 10, 11; Redemption Rules 1, 13, 18) ---

    [Theory]
    [InlineData(null, null, 60.0, false)]  // open, Ritual not done yet
    [InlineData(null, null, 120.0, true)]  // missed: window closed, Ritual never done
    [InlineData(10.0, null, 60.0, false)]  // session still running
    [InlineData(10.0, null, 600.0, false)] // still running past the end — its session hasn't ended
    [InlineData(10.0, 45.0, 60.0, true)]   // session ended early
    [InlineData(0.0, 120.0, 600.0, true)]  // ran to the end
    public void IsFinal_OnceTheSessionEndedOrTheBlockWasMissed(double? ritual, double? ended, double now, bool expected)
    {
        BlockWith(ritual, ended, cancelled: null).IsFinal(Min(now)).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, 600.0, 120.0)] // missed: the whole block
    [InlineData(null, null, 60.0, 0.0)]    // not missed yet
    [InlineData(0.0, 45.0, 600.0, 75.0)]   // on time, quit after 45 of 120
    [InlineData(40.0, 120.0, 600.0, 40.0)] // 40 min late, ran to the end: 80 credited
    [InlineData(40.0, 190.0, 600.0, 40.0)] // late and ran past the end: credit stops at the end
    [InlineData(40.0, null, 600.0, 0.0)]   // late, still running: not final, so nothing owed yet
    [InlineData(3.0, 120.0, 600.0, 0.0)]   // within grace, ran to the end
    [InlineData(0.0, 150.0, 600.0, 0.0)]   // force-ended after the end
    public void OwnDeficit_IsWhatTheBlockFailedToDeliverOnceFinal(double? ritual, double? ended, double now, double expectedMinutes)
    {
        BlockWith(ritual, ended, cancelled: null).OwnDeficit(Min(now)).Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Fact]
    public void OwnDeficit_OfAMissedRedemptionBlock_IsZero()
    {
        // Rule 13: no "redeem the redemption" chain.
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(ScheduledStart, ScheduledEnd),
            membership: TestContracts.Redemption(Guid.NewGuid()));

        block.OwnDeficit(Min(600)).Should().Be(TimeSpan.Zero);
    }

    // --- Outcome, flags, spans and credit across every shape a block can take ---
    // Minutes after the scheduled start (01:00–03:00, grace 5 min) for the Ritual stamp, the
    // session end, "now" and a cancellation (null = not stamped); then the expected outcome,
    // flags, WorkSlot and CreditedSlot (null = none). Pinned to FocusBlockOutcomeClassifier's
    // answers before it was deleted (Gap 95 slice 2).

    public static TheoryData<string, double?, double?, double, double?, FocusBlockOutcome?, bool, bool, double[]?, double[]?> Cases() => new()
    {
        // name                            ritual  ended  now   cancel outcome                              late   early  work          credited
        { "upcoming",                      null,   null,  -30,  null,  null,                                false, false, null,         null },
        { "open, before end",              null,   null,  119,  null,  null,                                false, false, null,         null },
        { "missed at end",                 null,   null,  120,  null,  FocusBlockOutcome.Missed,            false, false, null,         null },
        { "missed long after",             null,   null,  600,  null,  FocusBlockOutcome.Missed,            false, false, null,         null },
        { "cancelled, never started",      null,   null,  600,  30,    FocusBlockOutcome.Missed,            false, false, null,         null },
        { "ritual before start, running",  -2,     null,  60,   null,  FocusBlockOutcome.CompletedOnTime,   false, false, [0, 120],     [0, 60] },
        { "on time, running",              0,      null,  60,   null,  FocusBlockOutcome.CompletedOnTime,   false, false, [0, 120],     [0, 60] },
        { "grace boundary, running",       5,      null,  60,   null,  FocusBlockOutcome.CompletedOnTime,   false, false, [0, 120],     [0, 60] },
        { "grace, ran to end",             3,      120,   600,  null,  FocusBlockOutcome.CompletedOnTime,   false, false, [0, 120],     [0, 120] },
        { "just past grace, ran to end",   6,      120,   600,  null,  FocusBlockOutcome.StartedLate,       true,  false, [6, 120],     [6, 120] },
        { "late, running",                 30,     null,  60,   null,  FocusBlockOutcome.StartedLate,       true,  false, [30, 120],    [30, 60] },
        { "late, running past end",        30,     null,  600,  null,  FocusBlockOutcome.StartedLate,       true,  false, [30, 120],    [30, 120] },
        { "late, ended early",             30,     90,    600,  null,  FocusBlockOutcome.EndedEarly,        true,  true,  [30, 90],     [30, 90] },
        { "grace, ended early",            3,      90,    600,  null,  FocusBlockOutcome.EndedEarly,        false, true,  [0, 90],      [0, 90] },
        { "on time, ended early",          0,      45,    600,  null,  FocusBlockOutcome.EndedEarly,        false, true,  [0, 45],      [0, 45] },
        { "on time, force-ended after",    0,      150,   600,  null,  FocusBlockOutcome.CompletedOnTime,   false, false, [0, 120],     [0, 120] },
        { "late, ended right at ritual",   30,     30,    600,  null,  FocusBlockOutcome.EndedEarly,        true,  true,  null,         null },
        { "ritual at end",                 120,    null,  600,  null,  FocusBlockOutcome.Missed,            false, false, null,         null },
        { "ritual after end",              130,    null,  600,  null,  FocusBlockOutcome.Missed,            false, false, null,         null },
        { "ritual after end, ended",       130,    140,   600,  null,  FocusBlockOutcome.Missed,            false, false, null,         null },
        { "on time, cancelled mid-way",    0,      40,    600,  40,    FocusBlockOutcome.EndedEarly,        false, true,  [0, 40],      [0, 40] },
        { "on time, seconds in end",       0,      60.5,  600,  null,  FocusBlockOutcome.EndedEarly,        false, true,  [0, 60.5],    [0, 60] },
        { "late with seconds, ended",      6.25,   60.1,  600,  null,  FocusBlockOutcome.EndedEarly,        true,  true,  [6.25, 60.1], [6.25, 59.25] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Block_AnswersFromItsOwnStamps(string _, double? ritual, double? ended, double now, double? cancelled,
        FocusBlockOutcome? outcome, bool startedLate, bool endedEarly, double[]? work, double[]? credited)
    {
        var block = BlockWith(ritual, ended, cancelled);

        block.Outcome(Min(now)).Should().Be(outcome);
        block.StartedLate.Should().Be(startedLate);
        block.EndedEarly.Should().Be(endedEarly);
        ((object?)block.WorkSlot()).Should().Be(Span(work));
        ((object?)block.CreditedSlot(Min(now))).Should().Be(Span(credited));
    }

    [Fact]
    public void CreditedSlot_RunningWithinGraceWithSeconds_FloorsOnce()
    {
        // The one place credit differs from the deleted classifier (accepted, Gap 95 decision 2):
        // a running session within grace, seconds on the clock. The classifier was fed an elapsed
        // time already floored and floored again (9 min); the block cuts at "now" and floors once.
        var block = BlockWith(ritual: 2.5, ended: null, cancelled: null);   // Ritual 01:02:30

        block.CreditedSlot(Min(10))!.Length.Should().Be(TimeSpan.FromMinutes(10)); // now 01:10:00
    }

    [Theory]
    [InlineData(-10.0, false)]
    [InlineData(0.0, false)]
    [InlineData(5.0, false)]  // at the grace boundary: still on time
    [InlineData(5.01, true)]
    [InlineData(130.0, true)]
    public void IsLateAt_OnlyPastTheGracePeriod(double minutes, bool expected)
    {
        NewBlock().IsLateAt(Min(minutes)).Should().Be(expected);
    }

    [Fact]
    public void CreditedSlot_OfABlockCrossingMidnight_StartsAtTheScheduledStartWithinGrace()
    {
        var start = new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc);
        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), At.Slot(start, start.AddHours(2)));
        block.MarkRitualCompleted(At.Utc(start.AddMinutes(3)));
        block.MarkSessionEnded(At.Utc(start.AddHours(2)));

        block.CreditedSlot(At.Utc(start.AddHours(3))).Should().Be(At.Slot(start, start.AddHours(2)));
    }

    // --- helpers ---

    private static Moment Min(double minutesAfterStart) => At.Utc(ScheduledStart.AddMinutes(minutesAfterStart));

    private static TimeSlot? Span(double[]? minutes) => minutes is [var from, var to] ? new TimeSlot(Min(from), Min(to)) : null;

    private static FocusBlock BlockWith(double? ritual, double? ended, double? cancelled)
    {
        var block = NewBlock();
        if (ritual is { } r) block.MarkRitualCompleted(Min(r));
        if (ended is { } e) block.MarkSessionEnded(Min(e));
        if (cancelled is { } c) block.MarkCancelled(Min(c));
        return block;
    }
}
