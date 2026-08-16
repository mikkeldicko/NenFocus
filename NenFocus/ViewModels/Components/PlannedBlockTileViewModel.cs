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

    /// <summary>True for a FocusBlock whose Ritual was never completed and whose window already closed.</summary>
    public bool IsMissed { get; }

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
        IsMissed = dto.IsMissed;
    }
}
