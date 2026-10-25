using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FluentAssertions;
using static NenFocus.Tests.Headless.HeadlessTestKit;

namespace NenFocus.Tests.Headless;

/// <summary>
/// The shared stylesheet (Styles/Buttons.axaml, included in App.axaml) reaches every screen —
/// including ones that never defined a style themselves, like the contract wizard (Gap 89 Slice B).
/// A missing style is no error, so only rendering shows it.
/// </summary>
public class SharedStylesViewTests
{
    [AvaloniaFact]
    public void AButtonOnAnyScreen_GetsTheSharedLook()
    {
        var primary = new Button { Content = "Create", Classes = { "primary" } };
        var danger = new Button { Content = "Delete", Classes = { "danger-btn" } };
        Show(new StackPanel { Children = { primary, danger } });

        ((ISolidColorBrush)primary.Background!).Color.Should().Be(Color.Parse("#1A1A2E"));
        primary.FontSize.Should().Be(13);
        ((ISolidColorBrush)danger.Background!).Color.Should().Be(Color.Parse("#C0392B"), "Delete is solid red everywhere");
    }
}
