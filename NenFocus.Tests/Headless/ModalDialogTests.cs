using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FluentAssertions;
using NenFocus.Views.Controls;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// The one dialog every screen uses (Gap 89 Slice B): the screen gives the words and its buttons,
/// the dialog shows them — the error only when there is one.
/// </summary>
public class ModalDialogTests
{
    private static (Window Window, ModalDialog Dialog, Button Cancel, Button Delete) ShowDialog(string? error = null)
    {
        var cancel = new Button { Content = "Cancel", Classes = { "secondary" } };
        var delete = new Button { Content = "Delete", Classes = { "danger-btn" } };
        var dialog = new ModalDialog
        {
            Title = "Delete Preset",
            Message = "Delete \"Deep Work\"? This cannot be undone.",
            Error = error,
            Items = { cancel, delete },
        };
        return (Show(dialog), dialog, cancel, delete);
    }

    [AvaloniaFact]
    public void ShowsTheTitleTheMessageAndTheButtons_InOrder()
    {
        var (_, dialog, _, _) = ShowDialog();

        VisibleTexts(dialog).Should().ContainInOrder("Delete Preset", "Delete \"Deep Work\"? This cannot be undone.", "Cancel", "Delete");
    }

    [AvaloniaFact]
    public void ShowsNoError_UntilThereIsOne()
    {
        var (_, dialog, _, _) = ShowDialog();
        VisibleTexts(dialog).Should().NotContain(t => t.Contains("in use"));

        dialog.Error = "This preset is in use and cannot be deleted.";
        Settle();

        VisibleTexts(dialog).Should().Contain("This preset is in use and cannot be deleted.");
    }

    [AvaloniaFact]
    public void TheButtonsAreTheScreens_AndClick()
    {
        var clicked = 0;
        var (window, _, _, delete) = ShowDialog();
        delete.Click += (_, _) => clicked++;

        Click(window, delete);

        clicked.Should().Be(1);
    }

    [AvaloniaFact]
    public void TheButtonsSitOnTheRight()
    {
        var (_, dialog, cancel, delete) = ShowDialog();

        var row = cancel.GetVisualParent<StackPanel>()!;
        row.Orientation.Should().Be(Avalonia.Layout.Orientation.Horizontal);
        row.HorizontalAlignment.Should().Be(Avalonia.Layout.HorizontalAlignment.Right);
        delete.GetVisualParent<StackPanel>().Should().BeSameAs(row);
    }
}
