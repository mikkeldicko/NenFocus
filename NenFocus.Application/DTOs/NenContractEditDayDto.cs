namespace NenFocus.Application.DTOs;

/// <summary>One day of a contract being edited — its local window and its floor (null = rest day).</summary>
public sealed record NenContractEditDayDto(DateOnly Date, DateTime WindowStart, DateTime WindowEnd, double? FloorHours);
