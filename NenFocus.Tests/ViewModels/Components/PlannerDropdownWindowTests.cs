using FluentAssertions;
using NenFocus.ViewModels.Components;

namespace NenFocus.Tests.ViewModels.Components;

public class PlannerDropdownWindowTests
{
    // Mirrors TimeOptions: 00:00, 00:15, ..., 23:45, 24:00 (97 entries, index 0..96).
    private static readonly List<string> TimeOptions = BuildTimeOptions();

    private static List<string> BuildTimeOptions()
    {
        var list = new List<string>();
        for (var h = 0; h < 24; h++)
            for (var m = 0; m < 60; m += 15)
                list.Add($"{h:D2}:{m:D2}");
        list.Add("24:00");
        return list;
    }

    [Fact]
    public void Center_MidList_ReturnsTwoBeforeAndTwoAfter()
    {
        var centerIndex = TimeOptions.IndexOf("14:00");

        var window = PlannerDropdownWindow.Center(TimeOptions, centerIndex, 5);

        window.Should().Equal("13:30", "13:45", "14:00", "14:15", "14:30");
    }

    [Fact]
    public void Center_NearEndOfList_PadsBackward_MatchesConfirmedExample()
    {
        // The exact confirmed example: current value 23:45, only "24:00" exists after it,
        // so one extra entry is pulled from before instead of showing only 4.
        var centerIndex = TimeOptions.IndexOf("23:45");

        var window = PlannerDropdownWindow.Center(TimeOptions, centerIndex, 5);

        window.Should().Equal("23:00", "23:15", "23:30", "23:45", "24:00");
    }

    [Fact]
    public void Center_AtVeryLastItem_PadsBackwardToStillTotalFive()
    {
        var centerIndex = TimeOptions.IndexOf("24:00");

        var window = PlannerDropdownWindow.Center(TimeOptions, centerIndex, 5);

        window.Should().HaveCount(5);
        window.Should().Equal("23:00", "23:15", "23:30", "23:45", "24:00");
    }

    [Fact]
    public void Center_AtVeryFirstItem_PadsForwardToStillTotalFive()
    {
        var centerIndex = TimeOptions.IndexOf("00:00");

        var window = PlannerDropdownWindow.Center(TimeOptions, centerIndex, 5);

        window.Should().HaveCount(5);
        window.Should().Equal("00:00", "00:15", "00:30", "00:45", "01:00");
    }

    [Fact]
    public void Center_SecondItem_PadsForwardByOne()
    {
        var centerIndex = TimeOptions.IndexOf("00:15");

        var window = PlannerDropdownWindow.Center(TimeOptions, centerIndex, 5);

        window.Should().HaveCount(5);
        window.Should().Equal("00:00", "00:15", "00:30", "00:45", "01:00");
    }

    [Fact]
    public void Center_ListSmallerThanWindow_ReturnsWholeListUnchanged()
    {
        var small = new[] { "10:00", "10:30", "11:00" };

        var window = PlannerDropdownWindow.Center(small, 1, 5);

        window.Should().Equal(small);
    }
}
