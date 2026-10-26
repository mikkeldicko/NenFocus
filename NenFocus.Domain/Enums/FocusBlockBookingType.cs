namespace NenFocus.Domain.Enums;

/// <summary>
/// How a Focus block was booked (Gap 114, D2 — FHIR's <c>appointmentType</c>): Planned is a
/// ROUTINE booking made in advance; Spontaneous is a WALKIN created already arrived, never booked
/// ahead. Only a Planned block can be Missed or StartedLate — a Spontaneous block's Ritual is done
/// at creation, so it starts when it starts.
/// </summary>
public enum FocusBlockBookingType
{
    Planned,
    Spontaneous
}
