using System;
using CommunityToolkit.Mvvm.ComponentModel;
using NenFocus.Application.DTOs;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Components;

public partial class PlannedBlockTileViewModel : ViewModelBase
{
    public Guid Id { get; }
    public string PresetName { get; }
    public BlockPresetType BlockPresetType { get; }
    public DateTime Start { get; }
    public DateTime End { get; }
    public WebsiteFilterType WebsiteFilterType { get; }

    /// <summary>0 = Monday, 6 = Sunday.</summary>
    public int ColumnIndex { get; }

    /// <summary>Vertical offset in pixels from the top of the scrollable hour area.</summary>
    public double Top { get; }

    /// <summary>Tile height in pixels.</summary>
    public double Height { get; }

    public bool IsFocus => BlockPresetType == BlockPresetType.Focus;
    public bool IsBackground => BlockPresetType == BlockPresetType.Background;

    /// <summary>True for an unsealed Nen Contract draft block — never persisted, styled distinctly.</summary>
    public bool IsDraft { get; }

    /// <summary>FocusBlock.Outcome — null for Background blocks or a still-pending Focus block.</summary>
    public FocusBlockOutcome? Outcome { get; }

    /// <summary>True only for Missed — the sole outcome whose tile text is struck through (nothing happened at all).</summary>
    public bool IsMissed => Outcome == FocusBlockOutcome.Missed;

    /// <summary>The Ritual came past the grace period — independent of Outcome; a block can have
    /// started late and ended early, and the tile shows a label for each.</summary>
    public bool StartedLate { get; }

    /// <summary>The session ended before the scheduled end.</summary>
    public bool EndedEarly { get; }

    /// <summary>
    /// Redemption or Bonus — mutually exclusive by construction. Null for an ordinary block
    /// (committed or unrelated to any contract). Drives the tile's badge.
    /// </summary>
    public PlannedBlockContractBadge? ContractBadge { get; }

    /// <param name="localStart">Where the tile starts on the user's clock — computed by the Planner
    /// through the user's time zone (Gap 88), never read off the DTO's moments.</param>
    public PlannedBlockTileViewModel(
        PlannedBlockDto dto,
        DateTime localStart,
        DateTime localEnd,
        BlockPresetType blockPresetType,
        int columnIndex,
        double top,
        double height,
        bool isDraft = false)
    {
        Id = dto.Id;
        PresetName = dto.PresetName;
        BlockPresetType = blockPresetType;
        Start = localStart;
        End = localEnd;
        WebsiteFilterType = dto.WebsiteFilterType;
        ColumnIndex = columnIndex;
        Top = top;
        Height = height;
        IsDraft = isDraft;
        Outcome = dto.Outcome;
        StartedLate = dto.StartedLate;
        EndedEarly = dto.EndedEarly;
        ContractBadge = dto.ContractBadge;
    }
}
