using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.NenContracts.SweepNenContractLifecycle;

/// <summary>
/// Activates Scheduled contracts whose start date has arrived and completes Active
/// contracts whose end date has passed — one heartbeat tick, both housekeeping checks.
/// No input; "now" comes from ISystemClock, matching StartSessionCommandHandler's
/// precedent. Mirrors SweepMissedFocusBlocksCommand's shape.
/// </summary>
public sealed record SweepNenContractLifecycleCommand : ICommand<SweepNenContractLifecycleResult>;
