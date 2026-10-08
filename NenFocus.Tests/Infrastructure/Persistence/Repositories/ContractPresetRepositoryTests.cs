using FluentAssertions;
using NenFocus.Infrastructure.Persistence.Repositories;

namespace NenFocus.Tests.Infrastructure.Persistence.Repositories;

/// <summary>
/// The Name column's NOCASE collation is the only thing making "Daily Flex" and "daily flex" the
/// same name — the query compares plainly (Gap 113, R1).
/// </summary>
public class ContractPresetRepositoryTests : SqliteRepositoryTestBase
{
    private ContractPresetRepository CreateRepository() => new(Context);

    [Theory]
    [InlineData("daily flex")]
    [InlineData("DAILY FLEX")]
    [InlineData("Daily Flex")]
    public async Task ExistsWithNameAsync_IsCaseInsensitive(string candidate)
    {
        var exists = await CreateRepository().ExistsWithNameAsync(candidate); // "Daily Flex" is seeded

        exists.Should().BeTrue();
    }
}
