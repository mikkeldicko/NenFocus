using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetNenContractRecap;

/// <summary>
/// Structural facts + performance grade for a Completed or Broken contract — see
/// NenContractRecapDto. Returns null if the contract doesn't exist.
/// </summary>
public sealed record GetNenContractRecapQuery(Guid ContractId) : IQuery<NenContractRecapDto?>;
