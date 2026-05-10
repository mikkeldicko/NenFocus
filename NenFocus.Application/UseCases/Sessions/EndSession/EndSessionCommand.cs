using System;

namespace NenFocus.Application.UseCases.Sessions.EndSession;

public sealed record EndSessionCommand
{
    public Guid SessionId { get; init; }
}