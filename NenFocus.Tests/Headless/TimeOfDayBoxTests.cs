using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FluentAssertions;
using NenFocus.Views.Controls;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// The time field every form shares (Gap 89 Slice B). Its value changes only when the user is
/// done — leaving the field, Enter or a pick — never while typing, so End doesn't flicker
/// through half-typed times like 10:03.
/// </summary>
public class TimeOfDayBoxTests
{
    private sealed record Form(Window Window, TimeOfDayBox Field, TextBox Box, Popup DropDown, TextBox Elsewhere);

    private static Form ShowField(TimeSpan? value, TimeSpan? durationFrom = null, IReadOnlyList<TimeSpan>? options = null)
    {
        var field = new TimeOfDayBox { Value = value, DurationFrom = durationFrom };
        if (options is not null) field.Options = options;
        var elsewhere = new TextBox();
        var window = Show(new StackPanel { Children = { field, elsewhere } });
        return new Form(window, field,
            field.GetVisualDescendants().OfType<TextBox>().Single(),
            field.GetLogicalDescendants().OfType<Popup>().Single(),
            elsewhere);
    }

    private static void Leave(Form form)
    {
        form.Elsewhere.Focus();
        Settle();
    }

    private static bool ShowsInvalid(Form form) =>
        form.Box.GetVisualAncestors().OfType<Border>().First(b => b.Name == "Frame").Classes.Contains("invalid");

    [AvaloniaFact]
    public void ShowsItsValue()
    {
        var form = ShowField(new TimeSpan(9, 5, 0));

        form.Box.Text.Should().Be("09:05");
    }

    [AvaloniaFact]
    public void Typing_ChangesNothing_UntilTheFieldIsLeft()
    {
        var form = ShowField(new TimeSpan(13, 0, 0));

        Type(form.Window, form.Box, "10:3");
        form.Field.Value.Should().Be(new TimeSpan(13, 0, 0), "\"10:3\" is half-typed — nothing is committed while typing");
        form.Window.KeyTextInput("0");
        Settle();
        form.Field.Value.Should().Be(new TimeSpan(13, 0, 0));

        Leave(form);
        form.Field.Value.Should().Be(new TimeSpan(10, 30, 0));
    }

    [AvaloniaFact]
    public void Enter_KeepsAWholeTimeAsTyped()
    {
        var form = ShowField(new TimeSpan(13, 0, 0));

        Type(form.Window, form.Box, "10:07");
        PressKey(form.Box, Key.Enter);

        form.Field.Value.Should().Be(new TimeSpan(10, 7, 0), "a whole time is kept, not snapped to the nearest row");
        form.DropDown.IsOpen.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Enter_OnAPartialTime_TakesTheHighlightedRow()
    {
        var form = ShowField(new TimeSpan(13, 0, 0));

        Type(form.Window, form.Box, "17");
        PressKey(form.Box, Key.Enter);

        form.Field.Value.Should().Be(new TimeSpan(17, 0, 0));
        form.Box.Text.Should().Be("17:00");
    }

    [AvaloniaFact]
    public void LeavingWithTextThatIsNotATime_IsNoTime_AndShowsRed_UntilEditedAgain()
    {
        var form = ShowField(new TimeSpan(13, 0, 0));

        Type(form.Window, form.Box, "abc");
        Leave(form);

        form.Field.Value.Should().BeNull();
        form.Box.Text.Should().Be("abc", "what the user typed is left as typed");
        ShowsInvalid(form).Should().BeTrue();

        form.Box.Focus();
        Settle();
        ShowsInvalid(form).Should().BeFalse("no red while editing");
    }

    [AvaloniaFact]
    public void ArrowKeys_PickTheNextAndPreviousRow()
    {
        var form = ShowField(new TimeSpan(14, 7, 0));
        form.Box.Focus();
        Settle();

        PressKey(form.Box, Key.Down);
        form.Field.Value.Should().Be(new TimeSpan(14, 15, 0), "Down from an off-grid time lands on the next row");
        PressKey(form.Box, Key.Down);
        form.Field.Value.Should().Be(new TimeSpan(14, 30, 0));
        PressKey(form.Box, Key.Up);
        form.Field.Value.Should().Be(new TimeSpan(14, 15, 0));
    }

    [AvaloniaFact]
    public void FocusingOpensTheDropdown_AndEscapeClosesIt()
    {
        var form = ShowField(new TimeSpan(9, 0, 0));

        form.Box.Focus();
        Settle();
        form.DropDown.IsOpen.Should().BeTrue();

        PressKey(form.Box, Key.Escape);
        form.DropDown.IsOpen.Should().BeFalse();
    }

    [AvaloniaFact]
    public void AnEndField_LabelsEachRowWithItsLength_AndStepsByLength_PastMidnight()
    {
        var from = new TimeSpan(23, 0, 0);
        var ends = new[] { new TimeSpan(23, 30, 0), TimeSpan.Zero, new TimeSpan(0, 30, 0) };
        var form = ShowField(new TimeSpan(23, 30, 0), durationFrom: from, options: ends);
        form.Box.Focus();
        Settle();

        VisibleTexts(form.DropDown.Child!).Should().ContainInOrder("23:30", "(30 mins)", "00:00", "(1 hr)", "00:30", "(1.5 hrs)");

        PressKey(form.Box, Key.Down);
        form.Field.Value.Should().Be(TimeSpan.Zero, "midnight is longer than 23:30, so it comes next");
        PressKey(form.Box, Key.Down);
        form.Field.Value.Should().Be(new TimeSpan(0, 30, 0));
    }

    [AvaloniaFact]
    public void ClickingARow_PicksIt()
    {
        var form = ShowField(new TimeSpan(9, 0, 0), options: [new TimeSpan(9, 0, 0), new TimeSpan(9, 15, 0)]);
        form.Box.Focus();
        Settle();

        var row = form.DropDown.Child!.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "09:15");
        row.RaiseEvent(new PointerPressedEventArgs(row, new Pointer(0, PointerType.Mouse, true), row,
            default, 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        Settle();

        form.Field.Value.Should().Be(new TimeSpan(9, 15, 0));
        form.DropDown.IsOpen.Should().BeFalse();
    }

    [AvaloniaFact]
    public void AValueSetFromOutside_IsShown()
    {
        var form = ShowField(new TimeSpan(9, 0, 0));

        form.Field.Value = new TimeSpan(11, 45, 0);
        Settle();

        form.Box.Text.Should().Be("11:45");
    }
}
