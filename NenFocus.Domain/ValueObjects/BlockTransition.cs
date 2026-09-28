using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A boundary a planned block crossed as the clock moved, and the moment it lies at. Not a
/// domain event — nothing about the block changes; the clock simply passed a point the
/// block already defines. The block-side twin of <see cref="BreakTransition"/>.
/// </summary>
public sealed record BlockTransition(BlockTransitionKind Kind, DateTime At);
