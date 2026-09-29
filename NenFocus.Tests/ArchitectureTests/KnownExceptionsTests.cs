using FluentAssertions;
using Xunit.Sdk;

namespace NenFocus.Tests.ArchitectureTests;

public class KnownExceptionsTests
{
    [Fact]
    public void Check_PassesWhenViolationsAreExactlyTheExceptions()
    {
        var act = () => KnownExceptions.Check(["A.cs", "B.cs"], ["B.cs", "A.cs"]);

        act.Should().NotThrow();
    }

    [Fact]
    public void Check_FailsOnANewViolation()
    {
        var act = () => KnownExceptions.Check(["A.cs", "New.cs"], ["A.cs"]);

        act.Should().Throw<XunitException>().WithMessage("*New violation*New.cs*");
    }

    [Fact]
    public void Check_FailsOnAStaleException()
    {
        var act = () => KnownExceptions.Check(["A.cs"], ["A.cs", "Fixed.cs"]);

        act.Should().Throw<XunitException>().WithMessage("*no longer violates*Fixed.cs*");
    }
}
