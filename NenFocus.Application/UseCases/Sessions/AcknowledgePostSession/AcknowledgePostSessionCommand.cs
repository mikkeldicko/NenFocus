using NenFocus.Application.Interfaces;

namespace NenFocus.Application.UseCases.Sessions.AcknowledgePostSession;

public sealed record AcknowledgePostSessionCommand : ICommand<AcknowledgePostSessionResult>
{
    public Guid SessionId { get; init; }
}
