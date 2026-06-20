using System;
using CommunityToolkit.Mvvm.ComponentModel;
using NenFocus.Application.DTOs;
using NenFocus.Domain.Enums;

namespace NenFocus.ViewModels.Components;

/// <summary>
/// Represents one planned block rendered as a tile on the week grid.
/// Layout properties (Top, Height, ColumnIndex) are computed by the page VM
/// based on the block's start/end times relative to the visible hour range.
/// </summary>
public partial class PlannedBlockTileViewModel : ViewModelBase
{
    public Guid Id { get; }
    public string ProfileName { get; }
    public BlockType BlockType { get; }
    public DateTime Start { get; }
    public DateTime End { get; }
    public bool IsAllowlistMode { get; }

    /// <summary>0 = Monday, 6 = Sunday.</summary>
    public int ColumnIndex { get; }

    /// <summary>Vertical offset in pixels from the top of the scrollable hour area.</summary>
    public double Top { get; }

    /// <summary>Tile height in pixels.</summary>
    public double Height { get; }

    public string TimeRangeText =>
        $"{Start:HH:mm} â€“ {End:HH:mm}";

    public bool IsFocus => BlockType == BlockType.Ritual;
    public bool IsPassive => BlockType == BlockType.Background;

    public PlannedBlockTileViewModel(
        PlannedBlockDto dto,
        BlockType blockType,
        int columnIndex,
        double top,
        double height)
    {
        Id = dto.Id;
        ProfileName = dto.ProfileName;
        BlockType = blockType;
        Start = dto.Start;
        End = dto.End;
        IsAllowlistMode = dto.IsAllowlistMode;
        ColumnIndex = columnIndex;
        Top = top;
        Height = height;
    }
}
