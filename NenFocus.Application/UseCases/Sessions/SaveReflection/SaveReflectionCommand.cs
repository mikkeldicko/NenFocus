namespace NenFocus.Application.UseCases.Sessions.SaveReflection;

public sealed record SaveReflectionCommand
{
    public Guid SessionId { get; init; }
    public string Reflection { get; init; } = string.Empty;
    public int Rating { get; init; }
}