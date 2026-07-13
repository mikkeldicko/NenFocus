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
    public void RitualCompletedAtStart_IsCompletedOnTime()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ScheduledStart, asOf: ScheduledStart)
            .Should().Be(FocusBlockOutcome.CompletedOnTime);
    }

    [Fact]
    public void RitualCompletedBeforeStart_IsCompletedOnTime()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ScheduledStart.AddMinutes(-10), asOf: ScheduledStart)
            .Should().Be(FocusBlockOutcome.CompletedOnTime);
    }

    [Fact]
    public void RitualCompletedWithinGracePeriod_IsCompletedOnTime()
    {
        var ritualAt = ScheduledStart.AddMinutes(3); // within the 5-minute grace period

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, asOf: ritualAt)
            .Should().Be(FocusBlockOutcome.CompletedOnTime);
    }

    [Fact]
    public void RitualCompletedExactlyAtGraceBoundary_IsCompletedOnTime()
    {
        var ritualAt = ScheduledStart.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes);

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, asOf: ritualAt)
            .Should().Be(FocusBlockOutcome.CompletedOnTime);
    }

    [Fact]
    public void RitualCompletedJustPastGraceBoundary_IsStartedLate()
    {
        var ritualAt = ScheduledStart.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes).AddMinutes(1);

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, asOf: ritualAt)
            .Should().Be(FocusBlockOutcome.StartedLate);
    }

    [Fact]
    public void RitualCompletedMidWindow_IsStartedLate()
    {
        var ritualAt = ScheduledStart.AddHours(1); // 2:00, between 1:00 and 3:00

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, asOf: ritualAt)
            .Should().Be(FocusBlockOutcome.StartedLate);
    }

    [Fact]
    public void RitualCompletedAtEnd_IsMissed()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ScheduledEnd, asOf: ScheduledEnd)
            .Should().Be(FocusBlockOutcome.Missed);
    }

    [Fact]
    public void RitualCompletedAfterEnd_IsMissed()
    {
        var ritualAt = ScheduledEnd.AddMinutes(30);

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, asOf: ritualAt)
            .Should().Be(FocusBlockOutcome.Missed);
    }

    [Fact]
    public void NoRitual_AsOfAtEnd_IsMissed()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: null, asOf: ScheduledEnd)
            .Should().Be(FocusBlockOutcome.Missed);
    }

    [Fact]
    public void NoRitual_AsOfAfterEnd_IsMissed()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: null, asOf: ScheduledEnd.AddHours(1))
            .Should().Be(FocusBlockOutcome.Missed);
    }

    [Fact]
    public void NoRitual_InProgress_IsNull()
    {
        var asOf = ScheduledStart.AddHours(1); // 2:00 — window still open until 3:00

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: null, asOf: asOf)
            .Should().BeNull();
    }

    [Fact]
    public void NoRitual_JustBeforeEnd_IsNull()
    {
        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: null, asOf: ScheduledEnd.AddMinutes(-1))
            .Should().BeNull();
    }

    [Fact]
    public void NoRitual_FutureBlock_IsNull()
    {
        var asOf = ScheduledStart.AddMinutes(-30); // 12:30 — block hasn't started yet

        _classifier.Classify(ScheduledStart, ScheduledEnd, ritualCompletedAt: null, asOf: asOf)
            .Should().BeNull();
    }

    [Fact]
    public void CreditedFocusMinutes_WithinGracePeriod_CreditsFullScheduledDuration()
    {
        var ritualAt = ScheduledStart.AddMinutes(3);

        // Real elapsed time is short (started 3 min late), but grace period rounds up.
        var credited = _classifier.CreditedFocusMinutes(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, actualElapsedMinutes: 117);

        credited.Should().Be((ScheduledEnd - ScheduledStart).TotalMinutes); // full 120 minutes
    }

    [Fact]
    public void CreditedFocusMinutes_AtGraceBoundary_CreditsFullScheduledDuration()
    {
        var ritualAt = ScheduledStart.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes);

        var credited = _classifier.CreditedFocusMinutes(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, actualElapsedMinutes: 115);

        credited.Should().Be((ScheduledEnd - ScheduledStart).TotalMinutes);
    }

    [Fact]
    public void CreditedFocusMinutes_PastGracePeriod_CreditsOnlyActualElapsed()
    {
        var ritualAt = ScheduledStart.AddMinutes(FocusBlockOutcomeClassifier.GracePeriodMinutes + 1);

        var credited = _classifier.CreditedFocusMinutes(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: ritualAt, actualElapsedMinutes: 90);

        credited.Should().Be(90);
    }

    [Fact]
    public void CreditedFocusMinutes_Missed_CreditsZero()
    {
        var credited = _classifier.CreditedFocusMinutes(
            ScheduledStart, ScheduledEnd, ritualCompletedAt: null, actualElapsedMinutes: 0);

        credited.Should().Be(0);
    }
}
