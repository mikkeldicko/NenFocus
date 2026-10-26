using System.Runtime.CompilerServices;
using FluentAssertions;
using FluentAssertions.Formatting;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests;

/// <summary>
/// The one way tests make a <see cref="Moment"/> — <c>At.Utc(2026, 9, 29, 13, 0)</c> — so the
/// storage exit (<see cref="Moment.FromStorage"/>) stays out of individual tests (Gap 88/99, Q3).
/// </summary>
internal static class At
{
    public static Moment Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        Moment.FromStorage(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc));

    /// <summary>
    /// A test's existing UTC DateTime as a Moment. Unspecified is read as UTC (the database's
    /// convention); a local time throws — a test mixing them up should fail loudly.
    /// </summary>
    public static Moment Utc(DateTime utc) => utc.Kind == DateTimeKind.Local
        ? throw new ArgumentException("Expected a UTC time, got a local one.", nameof(utc))
        : Moment.FromStorage(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    /// <summary>
    /// A time zone where local time equals UTC — for tests that describe times as plain values and
    /// don't care about zones (the old "ToLocal(x) => x" fakes). Tests about a real zone build their own.
    /// </summary>
    public static UserTimeZone UtcZone { get; } = new(TimeZoneInfo.Utc);

    /// <summary>
    /// A TimeSlot between two UTC DateTimes — the one way tests build a block's slot, so
    /// <see cref="Moment.FromStorage"/> stays out of individual tests (Gap 88).
    /// </summary>
    public static TimeSlot Slot(DateTime startUtc, DateTime endUtc) => new(Utc(startUtc), Utc(endUtc));

    /// <summary>
    /// The slot's start as a Moment, read through the UTC zone — the tests' way to reach a
    /// block's scheduled endpoint now that Block.Start/End are gone (a slot's own
    /// Start/End are Domain-only, Gap 88).
    /// </summary>
    public static Moment Start(TimeSlot slot) => Utc(UtcZone.ToCalendarTimes(slot).Start);

    /// <summary>The slot's end as a Moment, read through the UTC zone.</summary>
    public static Moment End(TimeSlot slot) => Utc(UtcZone.ToCalendarTimes(slot).End);

    /// <summary>
    /// A planned window ending at <paramref name="endUtc"/> — for <c>Session.Start</c>/<c>RunLength</c>,
    /// which only read the window's end (a planned session runs until its block's end). The start is
    /// a day earlier because no test depends on it.
    /// </summary>
    public static TimeSlot PlannedWindowEnding(DateTime endUtc) => new(Utc(endUtc) - TimeSpan.FromDays(1), Utc(endUtc));

    // A Moment has no formatting of its own (deliberately — display goes through UserTimeZone),
    // so failure messages would read "Moment { }". Tests show the stored UTC value instead.
    [ModuleInitializer]
    internal static void RegisterFormatter() => Formatter.AddFormatter(new MomentFormatter());

    private sealed class MomentFormatter : IValueFormatter
    {
        public bool CanHandle(object value) => value is Moment;

        public void Format(object value, FormattedObjectGraph formattedGraph, FormattingContext context, FormatChild formatChild) =>
            formattedGraph.AddFragment(((Moment)value).ToStorage().ToString("yyyy-MM-dd HH:mm:ss'Z'"));
    }
}
