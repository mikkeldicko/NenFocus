using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A marker on a planned block's timeline (starting soon, started, ended) that the clock
/// passed, and the moment it lies at. Not a domain event — nothing about the block changes;
/// the clock simply passed a point the block already defines. The block-side counterpart of
/// <see cref="BreakTransition"/>, named a marker rather than a transition because
/// StartingSoon changes nothing about the block.
/// </summary>
public sealed record BlockMarker(BlockMarkerKind Kind, DateTime At);
