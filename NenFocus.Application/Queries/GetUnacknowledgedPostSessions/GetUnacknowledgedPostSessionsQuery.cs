using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Application.Queries.GetUnacknowledgedPostSessions;

public sealed record GetUnacknowledgedPostSessionsQuery : IQuery<IReadOnlyList<PostSessionDto>>;
