namespace NenFocus.Application.UseCases.Sessions.ResumeFromBreak;

public sealed record ResumeFromBreakResult(bool Success, string? ErrorMessage = null);