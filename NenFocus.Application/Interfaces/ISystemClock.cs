namespace NenFocus.Application.Interfaces;

public interface ISystemClock
{
    DateTime UtcNow { get; }

    /// <summary>
    /// Converts a UTC instant to the user's local wall-clock time — the single, testable
    /// seam every "which calendar day is this on" computation must go through. Calling
    /// DateTime.ToLocalTime() directly is untestable (it reads the host OS's real timezone)
    /// and has already produced several silent bugs where a call site simply forgot to
    /// convert at all before deriving a calendar day from a UTC value.
    /// </summary>
    DateTime ToLocal(DateTime utc);
}
