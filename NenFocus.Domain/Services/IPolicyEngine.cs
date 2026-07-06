using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

public interface IPolicyEngine
{
    ActivePolicySlice ComputeSlice(
        DateTime at,
        IEnumerable<BlockPreset> presets,
        IEnumerable<Session> sessions,
        IEnumerable<PlannedBlock> blocks);
}