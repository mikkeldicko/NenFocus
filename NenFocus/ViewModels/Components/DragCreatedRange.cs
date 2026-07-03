using System;

namespace NenFocus.ViewModels.Components;

/// <summary>Parameter passed from the code-behind to the ViewModel when a drag-create gesture completes.</summary>
public readonly record struct DragCreatedRange(int ColumnIndex, TimeSpan LocalStart, TimeSpan LocalEnd);
