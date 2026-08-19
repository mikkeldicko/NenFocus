namespace NenFocus.Application.Interfaces;

/// <summary>
/// Debug-only control surface for overriding what <see cref="ISystemClock"/> reports as
/// "now". Every consumer of <see cref="ISystemClock"/> observes the override transparently
/// on its next read; nothing else in the app needs to know this exists.
/// </summary>
public interface IDebugClockController
{
    bool IsOverridden { get; }

    void SetTo(DateTime utc);
    void AdvanceBy(TimeSpan amount);
    void Reset();

    /// <summary>Raised whenever the effective time changes — either the override moved, or a live second ticked over.</summary>
    event EventHandler? Changed;
}
