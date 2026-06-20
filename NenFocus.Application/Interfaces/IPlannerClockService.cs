namespace NenFocus.Application.Interfaces;

public interface IPlannerClockService
{
    event EventHandler? Tick;
    DateTime LocalNow { get; }
}
