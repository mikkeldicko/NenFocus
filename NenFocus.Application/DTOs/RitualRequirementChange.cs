namespace NenFocus.Application.DTOs;

/// <summary>
/// Carries which Focus block a ritual-requirement transition concerns. On a
/// transition to required=false caused by a block's window closing unfulfilled,
/// FocusBlockId identifies that block. It is null for transitions that aren't
/// about a specific block's forced window (e.g. a session starting/ending).
///
/// HandoffFromFocusBlockId is set only when Required=true replaces one pending
/// block with a different one directly (back-to-back scheduled blocks with no
/// gap) — it identifies the block being displaced, which never got its own
/// Required=false transition and must still be resolved (marked missed).
/// </summary>
public sealed record RitualRequirementChange(bool Required, Guid? FocusBlockId, Guid? HandoffFromFocusBlockId = null);
