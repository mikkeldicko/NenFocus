using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetActiveSession;

public sealed record GetActiveSessionQuery : IQuery<SessionDto?>;