using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.EndSession;

public sealed record EndSessionCommand : ICommand<EndSessionResult>
{
    public Guid SessionId { get; init; }
}