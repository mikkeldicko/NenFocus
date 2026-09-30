using FluentAssertions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

/// <summary>
/// A slot is half-open, [Start, End): it includes its start, not its end. So 10:00–11:00 and
/// 11:00–12:00 touch without overlapping, and at exactly 11:00 the first has ended.
/// </summary>
public class TimeSlotTests
{
    private static TimeSlot Slot(int startHour, int endHour, int startMinute = 0, int endMinute = 0)
        => new(At.Utc(2026, 9, 29, startHour, startMinute), At.Utc(2026, 9, 29, endHour, endMinute));

    [Fact]
    public void Constructor_EndBeforeStart_Throws()
    {
        var act = () => Slot(11, 10);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EndEqualsStart_Throws()
    {
        var act = () => Slot(10, 10);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Length_IsEndMinusStart()
        => Slot(10, 11, endMinute: 30).Length.Should().Be(TimeSpan.FromMinutes(90));

    [Fact]
    public void SameStartAndEnd_AreEqual()
        => Slot(10, 11).Should().Be(Slot(10, 11));

    [Theory]
    [InlineData(10, 11, 10, 11, true)]  // same slot
    [InlineData(10, 11, 10, 12, true)]  // same start
    [InlineData(10, 12, 11, 13, true)]  // partial
    [InlineData(10, 13, 11, 12, true)]  // one inside the other
    [InlineData(10, 11, 11, 12, false)] // touching: back-to-back is allowed
    [InlineData(11, 12, 10, 11, false)] // touching, other order
    [InlineData(10, 11, 12, 13, false)] // gap between
    public void Overlaps_SharesAtLeastOneMoment(int aStart, int aEnd, int bStart, int bEnd, bool expected)
    {
        Slot(aStart, aEnd).Overlaps(Slot(bStart, bEnd)).Should().Be(expected);
        Slot(bStart, bEnd).Overlaps(Slot(aStart, aEnd)).Should().Be(expected, "overlap is symmetric");
    }

    [Fact]
    public void Contains_IncludesStart_ExcludesEnd()
    {
        var slot = Slot(10, 11);

        slot.Contains(At.Utc(2026, 9, 29, 9, 59)).Should().BeFalse();
        slot.Contains(At.Utc(2026, 9, 29, 10)).Should().BeTrue();
        slot.Contains(At.Utc(2026, 9, 29, 10, 59)).Should().BeTrue();
        slot.Contains(At.Utc(2026, 9, 29, 11)).Should().BeFalse();
    }

    [Fact]
    public void HasStarted_FromStartOnward()
    {
        var slot = Slot(10, 11);

        slot.HasStarted(At.Utc(2026, 9, 29, 9, 59)).Should().BeFalse();
        slot.HasStarted(At.Utc(2026, 9, 29, 10)).Should().BeTrue();
        slot.HasStarted(At.Utc(2026, 9, 29, 12)).Should().BeTrue();
    }

    [Fact]
    public void HasEnded_FromEndOnward()
    {
        var slot = Slot(10, 11);

        slot.HasEnded(At.Utc(2026, 9, 29, 10, 59)).Should().BeFalse();
        slot.HasEnded(At.Utc(2026, 9, 29, 11)).Should().BeTrue("at exactly End the slot is over — the next may start");
        slot.HasEnded(At.Utc(2026, 9, 29, 12)).Should().BeTrue();
    }

    [Fact]
    public void AtItsEnd_ContainsHasEndedAndTheNextSlotAgree()
    {
        // The one boundary convention: at 11:00 this slot doesn't contain the moment, has ended,
        // and a slot starting at 11:00 has started and doesn't overlap it.
        var slot = Slot(10, 11);
        var next = Slot(11, 12);
        var eleven = At.Utc(2026, 9, 29, 11);

        slot.Contains(eleven).Should().BeFalse();
        slot.HasEnded(eleven).Should().BeTrue();
        next.HasStarted(eleven).Should().BeTrue();
        slot.Overlaps(next).Should().BeFalse();
    }

    [Fact]
    public void StartsIn_IsTimeUntilStart_NegativeOnceStarted()
    {
        var slot = Slot(10, 11);

        slot.StartsIn(At.Utc(2026, 9, 29, 9, 45)).Should().Be(TimeSpan.FromMinutes(15));
        slot.StartsIn(At.Utc(2026, 9, 29, 10, 5)).Should().Be(TimeSpan.FromMinutes(-5));
    }

    [Fact]
    public void Covering_WholeMinutes_IsExactlyThatLong()
        => TimeSlot.Covering(At.Utc(2026, 9, 29, 10), 30).Should().Be(Slot(10, 10, endMinute: 30));

    [Fact]
    public void Covering_FractionalMinutes_RoundsUpToTheNextWholeMinute()
    {
        // Gap 33: a fractional deficit must be covered in full, never fall short by tick rounding.
        var slot = TimeSlot.Covering(At.Utc(2026, 9, 29, 10), 31.896581196666666);

        slot.Should().Be(Slot(10, 10, endMinute: 32));
        slot.Length.TotalMinutes.Should().BeGreaterThanOrEqualTo(31.896581196666666);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Covering_NothingToCover_Throws(double minutes)
    {
        var act = () => TimeSlot.Covering(At.Utc(2026, 9, 29, 10), minutes);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── How far through a slot a moment is (Gap 88 last step: the session timer asks these) ──

    [Fact]
    public void SinceStart_IsTheTimeSinceTheStart_NegativeBeforeIt()
    {
        Slot(10, 12).SinceStart(At.Utc(2026, 9, 29, 10, 30)).Should().Be(TimeSpan.FromMinutes(30));
        Slot(10, 12).SinceStart(At.Utc(2026, 9, 29, 9, 45)).Should().Be(TimeSpan.FromMinutes(-15));
    }

    [Fact]
    public void RemainingAt_IsTheTimeLeft_NeverNegative()
    {
        Slot(10, 12).RemainingAt(At.Utc(2026, 9, 29, 11, 15)).Should().Be(TimeSpan.FromMinutes(45));
        Slot(10, 12).RemainingAt(At.Utc(2026, 9, 29, 12, 30)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ProgressAt_IsTheFractionPassed_HeldBetweenZeroAndOne()
    {
        Slot(10, 12).ProgressAt(At.Utc(2026, 9, 29, 10, 30)).Should().Be(0.25);
        Slot(10, 12).ProgressAt(At.Utc(2026, 9, 29, 9)).Should().Be(0.0);
        Slot(10, 12).ProgressAt(At.Utc(2026, 9, 29, 13)).Should().Be(1.0);
    }

    [Fact]
    public void TryCreate_ValidRange_IsTheSlot()
        => TimeSlot.TryCreate(At.Utc(2026, 9, 29, 10), At.Utc(2026, 9, 29, 11)).Should().Be(Slot(10, 11));

    [Theory]
    [InlineData(11, 10)]
    [InlineData(10, 10)]
    public void TryCreate_EmptyOrInvertedRange_IsNull(int startHour, int endHour)
        => TimeSlot.TryCreate(At.Utc(2026, 9, 29, startHour), At.Utc(2026, 9, 29, endHour)).Should().BeNull();

    [Fact]
    public void Enclosing_IsTheSmallestSlotContainingBoth_InEitherOrder()
    {
        TimeSlot.Enclosing(Slot(10, 11), Slot(13, 14)).Should().Be(Slot(10, 14));
        TimeSlot.Enclosing(Slot(13, 14), Slot(10, 11)).Should().Be(Slot(10, 14));
        TimeSlot.Enclosing(Slot(10, 14), Slot(11, 12)).Should().Be(Slot(10, 14));
    }

    [Fact]
    public void From_KeepsTheEnd_MovesTheStart()
        => Slot(10, 12).From(At.Utc(2026, 9, 29, 10, 30)).Should().Be(Slot(10, 12, startMinute: 30));

    [Fact]
    public void Until_KeepsTheStart_MovesTheEnd()
        => Slot(10, 12).Until(At.Utc(2026, 9, 29, 11, 15)).Should().Be(Slot(10, 11, endMinute: 15));

    [Fact]
    public void From_PastTheEnd_Throws()
    {
        var act = () => Slot(10, 11).From(At.Utc(2026, 9, 29, 11));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CompareTo_SortsByStartThenEnd()
    {
        var slots = new[] { Slot(11, 12), Slot(10, 12), Slot(10, 11) };

        slots.Order().Should().Equal(Slot(10, 11), Slot(10, 12), Slot(11, 12));
    }
}
