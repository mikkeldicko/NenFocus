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

    /// <summary>FocusBlockOutcomeClassifier.Classify's result — null for Background blocks or a still-pending Focus block.</summary>
    public FocusBlockOutcome? Outcome { get; }

    /// <summary>True only for Missed — the sole outcome whose tile text is struck through (nothing happened at all).</summary>
    public bool IsMissed => Outcome == FocusBlockOutcome.Missed;

    /// <summary>
    /// True when the ritual completed past the grace period, independent of Outcome — a block
    /// can be both StartedLate and EndedEarly (Outcome favors EndedEarly), so this lets the
    /// tile show both facts.
    /// </summary>
    public bool WasStartedLate { get; }

    /// <summary>True for a Focus block created to cover another block's deficit — renders with a dashed border.</summary>
    public bool IsMakeUp { get; }

    public PlannedBlockTileViewModel(
        PlannedBlockDto dto,
        BlockPresetType blockPresetType,
        int columnIndex,
        double top,
        double height,
        bool isDraft = false)
    {
        Id = dto.Id;
        PresetName = dto.PresetName;
        BlockPresetType = blockPresetType;
        Start = dto.Start;
        End = dto.End;
        WebsiteFilterType = dto.WebsiteFilterType;
        ColumnIndex = columnIndex;
        Top = top;
        Height = height;
        IsDraft = isDraft;
        Outcome = dto.Outcome;
        WasStartedLate = dto.WasStartedLate;
        IsMakeUp = dto.IsMakeUp;
    }
}
