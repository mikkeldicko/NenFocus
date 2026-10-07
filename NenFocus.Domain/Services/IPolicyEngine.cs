using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

public interface IPolicyEngine
{
    /// <summary>
    /// What should be blocked at <paramref name="at"/>. A session inside a break slot has
    /// its own preset lifted only if Session.LiftsBlockingAt says so — the session carries
    /// whether its contract keeps blocking through breaks (Gap 64), so no contract needs
    /// to be passed in or looked up here.
    /// </summary>
    ActivePolicySlice ComputeSlice(
        Moment at,
        IEnumerable<BlockPreset> presets,
        Session? running,
        IEnumerable<PlannedBlock> blocks);
}
