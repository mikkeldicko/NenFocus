using FluentAssertions;
using NenFocus.Infrastructure.Persistence.Repositories;

namespace NenFocus.Tests.Infrastructure.Persistence.Repositories;

/// <summary>
/// The Name column's NOCASE collation is the only thing making "Deep Work" and "deep work" the
/// same name — the query compares plainly (Gap 113, R1).
/// </summary>
public class BreakTimerPresetRepositoryTests : SqliteRepositoryTestBase
{
    private BreakTimerPresetRepository CreateRepository() => new(Context);

    [Theory]
    [InlineData("deep work")]
    [InlineData("DEEP WORK")]
    [InlineData("Deep Work")]
    public async Task ExistsWithNameAsync_IsCaseInsensitive(string candidate)
    {
        var exists = await CreateRepository().ExistsWithNameAsync(candidate); // "Deep Work" is seeded

        exists.Should().BeTrue();
    }
}
