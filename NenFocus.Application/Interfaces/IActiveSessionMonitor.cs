using NenFocus.Application.DTOs;

namespace NenFocus.Application.Interfaces;

public interface IActiveSessionMonitor
{
    TimeSpan SessionTimeRemaining { get; }
    double SessionProgress { get; }
    TimeSpan BreakTimeRemaining { get; }
    double BreakProgress { get; }

    event EventHandler? Tick;
    event EventHandler<PostSessionDto>? SessionAutoEnded;
}
