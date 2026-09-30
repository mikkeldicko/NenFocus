using System;

namespace NenFocus.ViewModels.Components;

/// <summary>Parameter passed from the code-behind to the ViewModel when a drag-move or drag-resize gesture completes.</summary>
public readonly record struct DragMoveResult(Guid BlockId, DateTime NewLocalStart, DateTime NewLocalEnd);
