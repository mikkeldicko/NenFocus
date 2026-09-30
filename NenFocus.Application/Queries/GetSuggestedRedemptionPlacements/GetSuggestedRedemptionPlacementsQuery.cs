using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.GetSuggestedRedemptionPlacements;

/// <summary>
/// Powers the active-contract banner's "Cover deficit" button — one entry per BlockPreset
/// that has an outstanding deficit coverable today, each sized to that preset's total
/// remaining deficit and pre-placed at a suggested time (see the handler for the exact
/// placement rule). Empty means nothing to cover right now, either because there's no
/// deficit at all or because every outstanding one belongs to a preset whose strictness
/// only allows same-day coverage on a day other than today.
/// </summary>
public sealed record GetSuggestedRedemptionPlacementsQuery(Guid ContractId, Moment AsOf)
    : IQuery<IReadOnlyList<SuggestedRedemptionPlacementDto>>;
