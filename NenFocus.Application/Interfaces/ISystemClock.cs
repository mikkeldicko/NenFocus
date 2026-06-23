namespace NenFocus.Application.Interfaces;

public interface ISystemClock
{
    DateTime UtcNow { get; }
}
