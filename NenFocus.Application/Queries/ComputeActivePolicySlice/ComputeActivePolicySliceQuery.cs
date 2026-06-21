using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.ComputeActivePolicySlice;

public record ComputeActivePolicySliceQuery(DateTime At) : IQuery<ActivePolicySlice>;