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
}
