using NenFocus.Application.Interfaces;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Application.Queries.ComputeActivePolicySlice;

public record ComputeActivePolicySliceQuery(Moment At) : IQuery<ActivePolicySlice>;