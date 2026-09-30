using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetFocusBlockAwaitingRitual;

/// <summary>
/// "Which Focus Block, if any, should push the user into the Ritual at <see cref="At"/>?"
/// Null when none should. Deliberately separate from ComputeActivePolicySliceQuery — the
/// slice answers what to block; this answers whether to prompt the Ritual (Gap 62).
/// </summary>
public sealed record GetFocusBlockAwaitingRitualQuery(Moment At) : IQuery<Guid?>;
