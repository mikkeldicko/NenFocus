using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.CheckBlockPresetLocked;

/// <summary>
/// Is this block preset locked right now — BlockPreset.IsLockedAt (Gap 89 Slice D)? Asked by the Update
/// and Delete preset validators, so the gathering is written once. False for a preset that doesn't exist:
/// nothing depends on it, and the handler reports it missing.
/// </summary>
public sealed record CheckBlockPresetLockedQuery(Guid PresetId) : IQuery<bool>;
