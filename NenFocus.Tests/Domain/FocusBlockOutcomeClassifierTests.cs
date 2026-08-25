using FluentAssertions;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;

namespace NenFocus.Tests.Domain;

public class FocusBlockOutcomeClassifierTests
{
    private static readonly DateTime ScheduledStart = new(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ScheduledEnd = new(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc);

    private readonly FocusBlockOutcomeClassifier _classifier = new();

    [Fact]
    public void IsLateStart_AtGraceBoundary_IsFalse()
    {
        var moment = ScheduledStart.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes);

        _classifier.IsLateStart(ScheduledStart, moment).Should().BeFalse();
    }

    [Fact]
    public void IsLateStart_JustPastGraceBoundary_IsTrue()
    {
        var moment = ScheduledStart.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes).AddMinutes(1);

        _classifier.IsLateStart(ScheduledStart, moment).Should().BeTrue();
    }

    [Fact]
    public void IsLateStart_BeforeScheduledStart_IsFalse()
    {
        _classifier.IsLateStart(ScheduledStart, ScheduledStart.AddMinutes(-10)).Should().BeFalse();
    }

    [Fact]
    public void RitualCompletedAtStart_IsCompletedOnTime()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ScheduledStart, sessionEndedAt: null, asOf: ScheduledStart)
            .Should().Be(FocusBlockOutcome.CompletedOnTime);
    }

    [Fact]
    public void RitualCompletedBeforeStart_IsCompletedOnTime()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ScheduledStart.AddMinutes(-10), sessionEndedAt: null, asOf: ScheduledStart)
            .Should().Be(FocusBlockOutcome.CompletedOnTime);
    }

    [Fact]
    public void RitualCompletedWithinGracePeriod_IsCompletedOnTime()
    {
        var ritualAt = ScheduledStart.AddMinutes(3); // within the 5-minute grace period

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, asOf: ritualAt)
            .Should().Be(FocusBlockOutcome.CompletedOnTime);
    }

    [Fact]
    public void RitualCompletedExactlyAtGraceBoundary_IsCompletedOnTime()
    {
        var ritualAt = ScheduledStart.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes);

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, asOf: ritualAt)
            .Should().Be(FocusBlockOutcome.CompletedOnTime);
    }

    [Fact]
    public void RitualCompletedJustPastGraceBoundary_IsStartedLate()
    {
        var ritualAt = ScheduledStart.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes).AddMinutes(1);

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, asOf: ritualAt)
            .Should().Be(FocusBlockOutcome.StartedLate);
    }

    [Fact]
    public void RitualCompletedMidWindow_IsStartedLate()
    {
        var ritualAt = ScheduledStart.AddHours(1); // 2:00, between 1:00 and 3:00

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, asOf: ritualAt)
            .Should().Be(FocusBlockOutcome.StartedLate);
    }

    [Fact]
    public void RitualCompletedAtEnd_IsMissed()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ScheduledEnd, sessionEndedAt: null, asOf: ScheduledEnd)
            .Should().Be(FocusBlockOutcome.Missed);
    }

    [Fact]
    public void RitualCompletedAfterEnd_IsMissed()
    {
        var ritualAt = ScheduledEnd.AddMinutes(30);

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, asOf: ritualAt)
            .Should().Be(FocusBlockOutcome.Missed);
    }

    [Fact]
    public void NoRitual_AsOfAtEnd_IsMissed()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: null, sessionEndedAt: null, asOf: ScheduledEnd)
            .Should().Be(FocusBlockOutcome.Missed);
    }

    [Fact]
    public void NoRitual_AsOfAfterEnd_IsMissed()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: null, sessionEndedAt: null, asOf: ScheduledEnd.AddHours(1))
            .Should().Be(FocusBlockOutcome.Missed);
    }

    [Fact]
    public void NoRitual_InProgress_IsNull()
    {
        var asOf = ScheduledStart.AddHours(1); // 2:00 — window still open until 3:00

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: null, sessionEndedAt: null, asOf: asOf)
            .Should().BeNull();
    }

    [Fact]
    public void NoRitual_JustBeforeEnd_IsNull()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: null, sessionEndedAt: null, asOf: ScheduledEnd.AddMinutes(-1))
            .Should().BeNull();
    }

    [Fact]
    public void NoRitual_FutureBlock_IsNull()
    {
        var asOf = ScheduledStart.AddMinutes(-30); // 12:30 — block hasn't started yet

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: null, sessionEndedAt: null, asOf: asOf)
            .Should().BeNull();
    }

    [Fact]
    public void RitualOnTimeButSessionEndedEarly_IsEndedEarly()
    {
        var sessionEndedAt = ScheduledStart.AddMinutes(45); // quit well before the 3:00 scheduled end

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ScheduledStart, sessionEndedAt: sessionEndedAt, asOf: sessionEndedAt)
            .Should().Be(FocusBlockOutcome.EndedEarly);
    }

    [Fact]
    public void RitualStartedLateAndSessionEndedEarly_IsEndedEarly()
    {
        var ritualAt = ScheduledStart.AddMinutes(20); // started late
        var sessionEndedAt = ritualAt.AddMinutes(15); // then also quit early

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: sessionEndedAt, asOf: sessionEndedAt)
            .Should().Be(FocusBlockOutcome.EndedEarly);
    }

    [Fact]
    public void SessionEndedExactlyAtScheduledEnd_IsNotEndedEarly()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ScheduledStart, sessionEndedAt: ScheduledEnd, asOf: ScheduledEnd)
            .Should().Be(FocusBlockOutcome.CompletedOnTime);
    }

    [Fact]
    public void NoRitual_SessionEndedAtIgnored_StillNull()
    {
        // sessionEndedAt is meaningless without a completed ritual — should never surface EndedEarly.
        var asOf = ScheduledStart.AddHours(1);

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: null, sessionEndedAt: ScheduledStart.AddMinutes(10), asOf: asOf)
            .Should().BeNull();
    }

    [Fact]
    public void CreditedFocusMinutes_WithinGracePeriod_CreditsFullScheduledDuration()
    {
        var ritualAt = ScheduledStart.AddMinutes(3);

        // Real elapsed time is short (started 3 min late), but grace period rounds up.
        var credited = _classifier.CreditedFocusMinutes(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, actualElapsedMinutes: 117);

        credited.Should().Be((ScheduledEnd - ScheduledStart).TotalMinutes); // full 120 minutes
    }

    [Fact]
    public void CreditedFocusMinutes_AtGraceBoundary_CreditsFullScheduledDuration()
    {
        var ritualAt = ScheduledStart.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes);

        var credited = _classifier.CreditedFocusMinutes(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, actualElapsedMinutes: 115);

        credited.Should().Be((ScheduledEnd - ScheduledStart).TotalMinutes);
    }

    [Fact]
    public void CreditedFocusMinutes_PastGracePeriod_CreditsOnlyActualElapsed()
    {
        var ritualAt = ScheduledStart.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes + 1);

        var credited = _classifier.CreditedFocusMinutes(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, actualElapsedMinutes: 90);

        credited.Should().Be(90);
    }

    [Fact]
    public void CreditedFocusMinutes_Missed_CreditsZero()
    {
        var credited = _classifier.CreditedFocusMinutes(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: null, sessionEndedAt: null, actualElapsedMinutes: 0);

        credited.Should().Be(0);
    }

    [Fact]
    public void CreditedFocusMinutes_EndedEarlyWithinGracePeriod_CreditsOnlyActualElapsed()
    {
        // Ritual was on time (would normally get the full-duration grace credit), but the
        // session ended early — early-end must never inherit the late-start forgiveness.
        var sessionEndedAt = ScheduledStart.AddMinutes(20);

        var credited = _classifier.CreditedFocusMinutes(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: ScheduledStart, sessionEndedAt: sessionEndedAt, actualElapsedMinutes: 20);

        credited.Should().Be(20);
    }

    [Fact]
    public void CreditedFocusMinutes_StartedLateAndRanPastScheduledEnd_CapsAtScheduledEnd()
    {
        // Ritual completed 19 minutes late (12:19 for a 12:00–15:00 block); the session
        // wasn't ended until well past the block's own scheduled end (e.g. via debug clock
        // skip). Credit must never exceed what was actually scheduled from the late start —
        // scheduledEnd (15:00) minus completedAt (12:19) = 161 minutes, not the raw elapsed
        // 180 minutes to the artificially-late EndedAt.
        var ritualAt = ScheduledStart.AddMinutes(19);

        var credited = _classifier.CreditedFocusMinutes(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, actualElapsedMinutes: 180);

        credited.Should().Be((ScheduledEnd - ritualAt).TotalMinutes);
    }

    [Fact]
    public void CreditedFocusMinutes_StartedLateAndEndedBeforeScheduledEnd_CreditsActualElapsed()
    {
        // Ordinary StartedLate case, ending within the scheduled window — unaffected by the cap.
        var ritualAt = ScheduledStart.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes + 1);

        var credited = _classifier.CreditedFocusMinutes(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, actualElapsedMinutes: 90);

        credited.Should().Be(90);
    }

    [Fact]
    public void CreditedWindow_WithinGracePeriod_IsTheFullScheduledWindow()
    {
        var ritualAt = ScheduledStart.AddMinutes(3);

        var window = _classifier.CreditedWindow(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, actualElapsedMinutes: 117);

        window.Should().Be((ScheduledStart, ScheduledEnd));
    }

    [Fact]
    public void CreditedWindow_Missed_IsNull()
    {
        var window = _classifier.CreditedWindow(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: null, sessionEndedAt: null, actualElapsedMinutes: 0);

        window.Should().BeNull();
    }

    [Fact]
    public void CreditedWindow_EndedEarly_IsTheActualWorkedWindow()
    {
        var sessionEndedAt = ScheduledStart.AddMinutes(20);

        var window = _classifier.CreditedWindow(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: ScheduledStart, sessionEndedAt: sessionEndedAt, actualElapsedMinutes: 20);

        window.Should().Be((ScheduledStart, ScheduledStart.AddMinutes(20)));
    }

    [Fact]
    public void CreditedWindow_StartedLateAndRanPastScheduledEnd_ClipsAtScheduledEnd()
    {
        var ritualAt = ScheduledStart.AddMinutes(19);

        var window = _classifier.CreditedWindow(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, actualElapsedMinutes: 180);

        window.Should().Be((ritualAt, ScheduledEnd));
    }

    [Fact]
    public void CreditedWindow_MatchesCreditedFocusMinutesDuration_AcrossEveryBranch()
    {
        // The refactor's core guarantee: CreditedFocusMinutes is derived from this window,
        // so the two can never disagree — verified directly rather than assumed, across
        // every branch the classifier has.
        var scenarios = new (DateTime? ritual, DateTime? ended, double elapsed)[]
        {
            (ScheduledStart.AddMinutes(3), null, 117),                                    // within grace
            (ScheduledStart.AddMinutes(19), null, 180),                                   // late, capped
            (ScheduledStart.AddMinutes(10), null, 90),                                    // late, uncapped
            (ScheduledStart, ScheduledStart.AddMinutes(20), 20),                          // ended early
            (null, null, 0),                                                              // missed
        };

        foreach (var (ritual, ended, elapsed) in scenarios)
        {
            var minutes = _classifier.CreditedFocusMinutes(ScheduledStart, ScheduledEnd, ritual, ended, elapsed);
            var window = _classifier.CreditedWindow(ScheduledStart, ScheduledEnd, ritual, ended, elapsed);

            var windowMinutes = window is { } w ? (w.End - w.Start).TotalMinutes : 0;
            windowMinutes.Should().Be(minutes);
        }
    }

    [Fact]
    public void CreditedWindow_CrossMidnightScheduledBlock_WithinGracePeriod_IsTheFullOvernightWindow()
    {
        var overnightStart = new DateTime(2026, 1, 1, 22, 0, 0, DateTimeKind.Utc);
        var overnightEnd = new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc); // 22:00-01:00, 3h
        var ritualAt = overnightStart.AddMinutes(2);

        var window = _classifier.CreditedWindow(
            overnightStart, overnightEnd, ritualCompletedAt: ritualAt, sessionEndedAt: null, actualElapsedMinutes: 178);

        window.Should().Be((overnightStart, overnightEnd));
    }
}
