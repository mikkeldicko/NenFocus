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
