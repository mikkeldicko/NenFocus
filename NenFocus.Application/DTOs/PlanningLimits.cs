using NenFocus.Domain.Entities;

namespace NenFocus.Application.DTOs;

/// <summary>
/// The Domain's planning limits, passed along for the UI, which may not read an entity
/// (MODULE_STANDARDS → No Domain Entity References). Holds no number of its own — each is the
/// Domain's. Becomes a query if a limit ever turns into a user setting (Gap 83).
/// </summary>
public static class PlanningLimits
{
    /// <summary>The longest a Focus block may be — <see cref="FocusBlock.MaxLength"/>.</summary>
    public static TimeSpan FocusBlockMaxLength => FocusBlock.MaxLength;
}
