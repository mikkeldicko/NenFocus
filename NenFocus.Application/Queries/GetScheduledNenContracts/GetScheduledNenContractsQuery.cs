using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetScheduledNenContracts;

/// <summary>
/// Every contract with NenContractStatus.Scheduled (not yet started), ordered by StartDate.
/// Feeds the Nen Contract popover's "Upcoming" section.
/// </summary>
public sealed record GetScheduledNenContractsQuery : IQuery<IReadOnlyList<ScheduledNenContractDto>>;
