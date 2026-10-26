using NenFocus.Domain.Enums;

namespace NenFocus.Domain.ValueObjects;

/// <summary>
/// A break boundary a session crossed: which edge, which kind of break, and the offset
/// from the session's StartedAt where it happened. Not a domain event — nothing about
/// the session changes at that moment; the clock simply moved past a point already
/// written in its BreakSchedule (Gap 63).
/// </summary>
public sealed record BreakTransition(BreakTransitionKind Kind, BreakKind BreakKind, TimeSpan At);
