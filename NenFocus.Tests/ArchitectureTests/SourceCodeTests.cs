using FluentAssertions;

namespace NenFocus.Tests.ArchitectureTests;

public class SourceCodeTests
{
    [Fact]
    public void WithoutComments_RemovesLineBlockAndDocComments()
    {
        var code = """
            /// <see cref="DateTime.UtcNow"/>
            var a = 1; // DateTime.UtcNow
            /* DateTime.UtcNow
               still a comment */ var b = 2;
            """;

        var stripped = SourceCode.WithoutComments(code);

        stripped.Should().NotContain("UtcNow");
        stripped.Should().Contain("var a = 1;").And.Contain("var b = 2;");
    }

    [Fact]
    public void WithoutComments_KeepsCommentMarkersInsideStrings()
    {
        var code = """
            var url = "http://localhost:5005"; var x = DateTime.UtcNow;
            var path = @"C:\temp\"; var y = DateTime.Now;
            var c = '"'; var z = DateTime.Today;
            var s = $"{a}/*"; var w = DateTime.UtcNow;
            """;

        var stripped = SourceCode.WithoutComments(code);

        stripped.Should().Be(code);
    }

    [Fact]
    public void WithoutComments_KeepsLineCount()
    {
        var code = "a\n/* one\ntwo */\nb // c\nd";

        SourceCode.WithoutComments(code).Split('\n').Should().HaveCount(5);
    }

    [Fact]
    public void Files_SkipsBuildOutputAndMigrations()
    {
        var files = SourceCode.Files("NenFocus.Infrastructure").Select(f => f.Path).ToList();

        files.Should().Contain("NenFocus.Infrastructure/Services/SystemClock.cs");
        files.Should().NotContain(p => p.Contains("/obj/") || p.Contains("/bin/") || p.Contains("/Migrations/"));
    }
}
