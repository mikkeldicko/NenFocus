using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetAllContractPresets;

public sealed record GetAllContractPresetsQuery : IQuery<IReadOnlyList<ContractPresetDto>>;
