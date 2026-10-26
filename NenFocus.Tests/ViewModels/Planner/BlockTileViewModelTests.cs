using FluentAssertions;
using NenFocus.Domain.Enums;
using NenFocus.ViewModels.Planner;

namespace NenFocus.Tests.ViewModels.Planner;

public class BlockTileViewModelTests
{
    [Fact]
    public void ForDraft_IsADraftWithNothingThatOnlyASavedBlockHas()
    {
        // A draft isn't saved: no outcome, no late/early marks, no contract badge (Gap 89 Slice B).
        var id = Guid.NewGuid();
        var tile = BlockTileViewModel.ForDraft(
            id, "Deep work", BlockPresetType.Focus,
            new DateTime(2026, 10, 26, 9, 0, 0), new DateTime(2026, 10, 26, 10, 0, 0), columnIndex: 0, top: 504, height: 56);

        tile.Id.Should().Be(id);
        tile.PresetName.Should().Be("Deep work");
        tile.IsFocus.Should().BeTrue();
        tile.IsDraft.Should().BeTrue();
        tile.Outcome.Should().BeNull();
        tile.StartedLate.Should().BeFalse();
        tile.EndedEarly.Should().BeFalse();
        tile.ContractRole.Should().BeNull();
    }
}
