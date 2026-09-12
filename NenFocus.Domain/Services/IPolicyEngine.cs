using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Domain.Services;

public interface IPolicyEngine
{
    /// <summary>
    /// contracts is used solely to resolve a Focus Block's PauseEnforcementDuringBreak
    /// setting when its Session is OnBreak (via PlannedBlock.ContractId) — see
    /// NenContract.PauseEnforcementDuringBreak. A block with no ContractId (an
    /// ordinary block, or a Bonus block whose Start falls inside a contract's date
    /// range without being linked to it) always falls back to the global Settings
    /// default, never the contract's setting. That global default is not yet a real,
    /// persisted value — Settings doesn't exist yet — so until it is built, the
    /// effective default is hardcoded permissive (pausing is allowed) wherever no
    /// contract governs the block; a contract can only ever narrow that toward
    /// stricter (keep blocking), never loosen it further.
    ///
    /// Optional and defaults to none: no caller outside the one enforcement pipeline
    /// that actually needs contract-aware pause resolution (ComputeActivePolicySliceQueryHandler)
    /// has any reason to resolve or pass NenContract entities just to call this method.
    /// </summary>
    ActivePolicySlice ComputeSlice(
        DateTime at,
        IEnumerable<BlockPreset> presets,
        IEnumerable<Session> sessions,
        IEnumerable<PlannedBlock> blocks,
        IEnumerable<NenContract>? contracts = null);
}