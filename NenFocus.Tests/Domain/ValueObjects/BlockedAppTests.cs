using FluentAssertions;
using NenFocus.Domain.Enums;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Tests.Domain.ValueObjects;

/// <summary>
/// An app is its process name — the one definition the preset, a session's extra apps, the slice and
/// the kill sweep share (Gap 112, I5) — and a critical system process can never be one.
/// </summary>
public class BlockedAppTests
{
    [Theory]
    [InlineData("  chrome  ", "chrome")]
    [InlineData("Steam", "steam")]
    [InlineData("Chrome.EXE", "chrome")]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", "steam")]
    public void Constructor_KeepsOnlyTheProcessName(string picked, string processName)
    {
        new BlockedApp(picked).ProcessName.Should().Be(processName);
    }

    [Fact]
    public void TwoWaysOfPickingTheSameApp_AreEqual()
    {
        new BlockedApp(@"C:\Games\Steam.exe").Should().Be(new BlockedApp("steam"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".exe")]
    public void Constructor_NoName_Throws(string? picked)
    {
        var act = () => new BlockedApp(picked!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("Explorer.exe")]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData("taskmgr")]
    [InlineData("lsass")]
    [InlineData("csrss")]
    [InlineData("wininit")]
    [InlineData("services")]
    [InlineData("winlogon")]
    [InlineData("smss")]
    [InlineData("svchost")]
    [InlineData("fontdrvhost")]
    [InlineData("dwm")]
    [InlineData("nenfocus")]
    public void Constructor_CriticalProcess_Throws(string picked)
    {
        var act = () => new BlockedApp(picked);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("", BlockedAppRefusal.Empty)]
    [InlineData(null, BlockedAppRefusal.Empty)]
    [InlineData(@"C:\Windows\explorer.exe", BlockedAppRefusal.CriticalProcess)]
    public void Check_GivesTheReasonWithoutThrowing(string? picked, BlockedAppRefusal reason)
    {
        BlockedApp.Check(picked).Should().Be(reason);
    }

    [Fact]
    public void Check_AnOrdinaryApp_HasNoReason()
    {
        BlockedApp.Check(@"C:\Games\steam.exe").Should().BeNull();
    }

    [Fact]
    public void NameOf_GivesTheProcessNameEvenForACriticalProcess()
    {
        // What the picker shows before the preset is saved; refusing is Check's job.
        BlockedApp.NameOf(@"C:\Windows\Explorer.exe").Should().Be("explorer");
    }

    [Theory]
    [InlineData("Steam", true)]
    [InlineData("steam", true)]
    [InlineData("steamwebhelper", false)]
    public void Matches_ARunningProcessByName(string runningProcess, bool matches)
    {
        new BlockedApp("steam").Matches(runningProcess).Should().Be(matches);
    }

    [Fact]
    public void ToString_ReturnsProcessName()
    {
        new BlockedApp("chrome").ToString().Should().Be("chrome");
    }
}
