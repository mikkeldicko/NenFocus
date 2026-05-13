using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.SaveReflection;

public sealed record SaveReflectionCommand : ICommand<SaveReflectionResult>
{
    public Guid SessionId { get; init; }
    public string Reflection { get; init; } = string.Empty;
    public int Rating { get; init; }
}