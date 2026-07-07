using FluentAssertions;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain;

public class PlannedBlockTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);

    private static FocusBlock CreateFocusBlock(DateTime? start = null, DateTime? end = null)
        => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), start ?? Start, end ?? End);

    private static BackgroundBlock CreateBackgroundBlock(DateTime? start = null, DateTime? end = null)
        => BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), start ?? Start, end ?? End);

    [Fact]
    public void FocusBlock_Create_EndBeforeStart_Throws()
    {
        var act = () => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), End, Start);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FocusBlock_Create_EndEqualsStart_Throws()
    {
        var act = () => FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, Start);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FocusBlock_Create_Valid_HasNoRitualCompletionYet()
    {
        var block = CreateFocusBlock();

        block.RitualCompletedAt.Should().BeNull();
    }

    [Fact]
    public void FocusBlock_MarkRitualCompleted_SetsTimestamp()
    {
        var block = CreateFocusBlock();
        var completedAt = DateTime.UtcNow;

        block.MarkRitualCompleted(completedAt);

        block.RitualCompletedAt.Should().Be(completedAt);
    }

    [Fact]
    public void FocusBlock_MarkRitualCompleted_Twice_Throws()
    {
        var block = CreateFocusBlock();
        block.MarkRitualCompleted(DateTime.UtcNow);

        var act = () => block.MarkRitualCompleted(DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BackgroundBlock_Create_EndBeforeStart_Throws()
    {
        var act = () => BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), End, Start);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BackgroundBlock_Create_Valid_SetsWindow()
    {
        var block = CreateBackgroundBlock();

        block.Start.Should().Be(Start);
        block.End.Should().Be(End);
    }

    [Theory]
    [InlineData(9, 30, 10, 30, true)]   // overlaps start
    [InlineData(10, 30, 11, 30, true)]  // overlaps end
    [InlineData(10, 15, 10, 45, true)]  // fully contained
    [InlineData(8, 0, 9, 0, false)]     // strictly before
    [InlineData(11, 0, 12, 0, false)]   // strictly after
    [InlineData(8, 0, 10, 0, false)]    // touches start boundary only
    public void OverlapsWith_VariousRanges(int otherStartHour, int otherStartMinute,
        int otherEndHour, int otherEndMinute, bool expectedOverlap)
    {
        var block = CreateFocusBlock();
        var other = CreateBackgroundBlock(
            new DateTime(2026, 1, 1, otherStartHour, otherStartMinute, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, otherEndHour, otherEndMinute, 0, DateTimeKind.Utc));

        block.OverlapsWith(other).Should().Be(expectedOverlap);
    }

    [Fact]
    public void OverlapsWith_TouchingEndBoundary_IsNotAnOverlap()
    {
        var block = CreateFocusBlock();
        var adjacent = CreateBackgroundBlock(End, End.AddHours(1));

        block.OverlapsWith(adjacent).Should().BeFalse();
    }

    [Fact]
    public void IsActive_AtStart_IsTrue()
        => CreateFocusBlock().IsActive(Start).Should().BeTrue();

    [Fact]
    public void IsActive_AtEnd_IsFalse()
        => CreateFocusBlock().IsActive(End).Should().BeFalse();

    [Fact]
    public void IsActive_BeforeStart_IsFalse()
        => CreateFocusBlock().IsActive(Start.AddMinutes(-1)).Should().BeFalse();

    [Fact]
    public void IsActive_MidWindow_IsTrue()
        => CreateFocusBlock().IsActive(Start.AddMinutes(30)).Should().BeTrue();

    [Fact]
    public void GetDuration_ReturnsEndMinusStart()
        => CreateFocusBlock().GetDuration().Should().Be(TimeSpan.FromHours(1));

    [Fact]
    public void MinutesUntilStart_ReturnsWholeMinutesToStart()
        => CreateFocusBlock().MinutesUntilStart(Start.AddMinutes(-15)).Should().Be(15);

    [Fact]
    public void Reschedule_Valid_UpdatesStartAndEnd()
    {
        var block = CreateFocusBlock();
        var newStart = Start.AddHours(2);
        var newEnd = End.AddHours(2);

        block.Reschedule(newStart, newEnd);

        block.Start.Should().Be(newStart);
        block.End.Should().Be(newEnd);
    }

    [Fact]
    public void Reschedule_EndBeforeStart_ThrowsAndLeavesWindowUnchanged()
    {
        var block = CreateFocusBlock();

        var act = () => block.Reschedule(End, Start);

        act.Should().Throw<ArgumentException>();
        block.Start.Should().Be(Start);
        block.End.Should().Be(End);
    }

    [Fact]
    public void TrimEnd_Valid_UpdatesEnd()
    {
        var block = CreateFocusBlock();
        var newEnd = Start.AddMinutes(45);

        block.TrimEnd(newEnd);

        block.End.Should().Be(newEnd);
    }

    [Fact]
    public void TrimEnd_BeforeOrAtStart_Throws()
    {
        var block = CreateFocusBlock();

        var act = () => block.TrimEnd(Start);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssignPreset_UpdatesBlockPresetId()
    {
        var block = CreateFocusBlock();
        var newPresetId = Guid.NewGuid();

        block.AssignPreset(newPresetId);

        block.BlockPresetId.Should().Be(newPresetId);
    }

    [Fact]
    public void FocusBlock_Create_WithoutContractId_DefaultsToNull()
    {
        CreateFocusBlock().ContractId.Should().BeNull();
    }

    [Fact]
    public void FocusBlock_Create_WithContractId_SetsIt()
    {
        var contractId = Guid.NewGuid();

        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End, contractId: contractId);

        block.ContractId.Should().Be(contractId);
    }

    [Fact]
    public void BackgroundBlock_Create_WithoutContractId_DefaultsToNull()
    {
        CreateBackgroundBlock().ContractId.Should().BeNull();
    }

    [Fact]
    public void BackgroundBlock_Create_WithContractId_SetsIt()
    {
        var contractId = Guid.NewGuid();

        var block = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End, contractId: contractId);

        block.ContractId.Should().Be(contractId);
    }

    [Fact]
    public void AssignContract_UpdatesContractId()
    {
        var block = CreateFocusBlock();
        var contractId = Guid.NewGuid();

        block.AssignContract(contractId);

        block.ContractId.Should().Be(contractId);
    }

    [Fact]
    public void AssignContract_Null_ClearsContractId()
    {
        var block = CreateFocusBlock();
        block.AssignContract(Guid.NewGuid());

        block.AssignContract(null);

        block.ContractId.Should().BeNull();
    }

    [Fact]
    public void FocusBlock_Create_WithoutMakeUpForEntries_IsEmpty()
    {
        CreateFocusBlock().MakeUpForEntries.Should().BeEmpty();
    }

    [Fact]
    public void FocusBlock_Create_WithMakeUpForEntries_SetsThem()
    {
        var entries = new[] { new MakeUpForEntry(Guid.NewGuid(), 30) };

        var block = FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Start, End, makeUpForEntries: entries);

        block.MakeUpForEntries.Should().BeEquivalentTo(entries);
    }

    [Fact]
    public void BackgroundBlock_Create_MakeUpForEntries_AlwaysEmpty()
    {
        CreateBackgroundBlock().MakeUpForEntries.Should().BeEmpty();
    }
}
