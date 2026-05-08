namespace NenFocus.Application.DTOs;

/// <summary>
/// The computed enforcement snapshot for a given moment in time.
/// Produced by ComputeActivePolicySliceQuery and consumed by IEnforcementService.
/// This is what gets handed to the OS-level blocker.
/// </summary>

public sealed record PolicySliceDto
{
    // Add this property
    public Guid? ActiveSessionId { get; init; }

    public DateTime SliceStart { get; init; }
    public DateTime SliceEnd { get; init; }
    public bool WebsiteAllowlistMode { get; init; }
    public IReadOnlySet<string> AllowedSites { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> BlockedSites { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> BlockedApps { get; init; } = new HashSet<string>();

    public bool IsEmpty =>
        !WebsiteAllowlistMode &&
        BlockedSites.Count == 0 &&
        BlockedApps.Count == 0;
}