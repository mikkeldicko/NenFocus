using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetUnacknowledgedPostContracts;

public sealed record GetUnacknowledgedPostContractsQuery : IQuery<IReadOnlyList<NenContractRecapDto>>;
