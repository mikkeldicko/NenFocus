using NenFocus.Domain.Aggregates;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

public interface IPolicyEngine
{
    ActivePolicySlice ComputeSlice(
        DateTime at,
        IEnumerable<BlockProfile> profiles,
        IEnumerable<Session> sessions,
        IEnumerable<PlannedBlock> blocks);
}