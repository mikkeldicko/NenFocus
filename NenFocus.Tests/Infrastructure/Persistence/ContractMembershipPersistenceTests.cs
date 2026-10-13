using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NenFocus.Domain.Entities;
using NenFocus.Domain.ValueObjects;
using NenFocus.Infrastructure.Persistence;
using NenFocus.Infrastructure.Persistence.Repositories;
using TestAt = NenFocus.Tests.At;

namespace NenFocus.Tests.Infrastructure.Persistence;

/// <summary>
/// Gap 97: a block's membership (contract id + role) is stored on the block's own row, in the
/// ContractId column that was already there plus ContractRole. Read back through a second
/// context, so each check is a real round trip.
/// </summary>
public class ContractMembershipPersistenceTests : SqliteRepositoryTestBase
{
    private static readonly DateTime Start = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
    // A new slot each time: a slot, like a membership, is stored as part of one block's row.
    private static TimeSlot Slot => TestAt.Slot(Start, Start.AddHours(1));

    private static FocusBlock Focus(ContractMembership? membership) =>
        FocusBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Slot, membership: membership);

    private async Task<PlannedBlock> ReloadAsync(Guid id)
    {
        await using var fresh = CreateFreshContext();
        return await fresh.PlannedBlocks.SingleAsync(b => b.Id == id);
    }

    [Fact]
    public async Task NoMembershipAndEveryRole_RoundTrip_OnBothBlockTypes()
    {
        var contractId = Guid.NewGuid();
        var plain = Focus(null);
        var committed = Focus(TestContracts.Committed(contractId));
        var redemption = Focus(TestContracts.Redemption(contractId));
        var bonus = Focus(TestContracts.Bonus(contractId));
        var background = BackgroundBlock.Create(Guid.NewGuid(), Guid.NewGuid(), Slot, membership: TestContracts.Bonus(contractId));

        Context.AddRange(plain, committed, redemption, bonus, background);
        await Context.SaveChangesAsync();

        (await ReloadAsync(plain.Id)).Membership.Should().BeNull();
        (await ReloadAsync(committed.Id)).Membership.Should().Be(TestContracts.Committed(contractId));
        (await ReloadAsync(redemption.Id)).Membership.Should().Be(TestContracts.Redemption(contractId));
        (await ReloadAsync(bonus.Id)).Membership.Should().Be(TestContracts.Bonus(contractId));
        (await ReloadAsync(background.Id)).Membership.Should().Be(TestContracts.Bonus(contractId));
    }

    [Fact]
    public async Task OneMembershipGivenToSeveralBlocks_IsSavedOnEachOfThem()
    {
        // A split's halves and a drafted batch all receive the same membership.
        var membership = TestContracts.Committed(Guid.NewGuid());
        var first = Focus(membership);
        var second = Focus(membership);
        Context.AddRange(first, second);
        await Context.SaveChangesAsync();

        (await ReloadAsync(first.Id)).Membership.Should().Be(membership);
        (await ReloadAsync(second.Id)).Membership.Should().Be(membership);
    }

    [Fact]
    public async Task GetForContractAsync_ReturnsEveryMemberOfThatContract_AndNothingElse()
    {
        var contractId = Guid.NewGuid();
        var committed = Focus(TestContracts.Committed(contractId));
        var bonus = Focus(TestContracts.Bonus(contractId));
        Context.AddRange(committed, bonus, Focus(null), Focus(TestContracts.Committed(Guid.NewGuid())));
        await Context.SaveChangesAsync();

        await using var fresh = CreateFreshContext();
        var members = await new PlannedBlockRepository(fresh).GetForContractAsync(contractId);

        members.Select(b => b.Id).Should().BeEquivalentTo([committed.Id, bonus.Id]);
    }

    [Fact]
    public async Task LeavingAndJoining_ThroughTheRepository_AreSaved()
    {
        var contractId = Guid.NewGuid();
        var leaver = Focus(TestContracts.Bonus(contractId));
        var joiner = Focus(null);
        Context.AddRange(leaver, joiner);
        await Context.SaveChangesAsync();

        await using (var context = CreateFreshContext())
        {
            var repository = new PlannedBlockRepository(context);
            var leaving = (await repository.GetByIdAsync(leaver.Id))!;
            var joining = (await repository.GetByIdAsync(joiner.Id))!;
            leaving.Leave();
            joining.Join(TestContracts.Bonus(contractId));
            await context.SaveChangesAsync();
        }

        (await ReloadAsync(leaver.Id)).Membership.Should().BeNull();
        (await ReloadAsync(joiner.Id)).Membership.Should().Be(TestContracts.Bonus(contractId));
    }

    [Fact]
    public async Task Migration_GivesEveryBlockThatHasAContractItsRole()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new NenFocusDbContext(
            new DbContextOptionsBuilder<NenFocusDbContext>().UseSqlite(connection).Options);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync("20260928204720_BackfillFocusBlockSessionEndedAt");
        var contractId = Guid.NewGuid();
        var drafted = Guid.NewGuid();
        var redeeming = Guid.NewGuid();
        var draftedBackground = Guid.NewGuid();
        var plain = Guid.NewGuid();
        const string insert =
            "INSERT INTO PlannedBlocks (Id, BlockKind, BlockPresetId, Start, \"End\", ContractId, RedemptionEntries) " +
            "VALUES ({0}, {1}, {2}, '2026-01-02 10:00:00', '2026-01-02 11:00:00', {3}, {4});";
        await context.Database.ExecuteSqlRawAsync(insert, drafted, "Focus", Guid.NewGuid(), contractId, "[]");
        await context.Database.ExecuteSqlRawAsync(insert, redeeming, "Focus", Guid.NewGuid(), contractId,
            $"[{{\"MissedBlockId\":\"{drafted}\",\"MinutesCovered\":30}}]");
        await context.Database.ExecuteSqlRawAsync(insert, draftedBackground, "Background", Guid.NewGuid(), contractId, "[]");
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO PlannedBlocks (Id, BlockKind, BlockPresetId, Start, \"End\", RedemptionEntries) " +
            "VALUES ({0}, 'Focus', {1}, '2026-01-02 12:00:00', '2026-01-02 13:00:00', '[]');", plain, Guid.NewGuid());

        await migrator.MigrateAsync();

        async Task<ContractMembership?> MembershipOf(Guid id) =>
            (await context.PlannedBlocks.AsNoTracking().SingleAsync(b => b.Id == id)).Membership;
        (await MembershipOf(drafted)).Should().Be(TestContracts.Committed(contractId));
        (await MembershipOf(redeeming)).Should().Be(TestContracts.Redemption(contractId));
        (await MembershipOf(draftedBackground)).Should().Be(TestContracts.Committed(contractId));
        (await MembershipOf(plain)).Should().BeNull();

        // No member is left without a role — such a row would load as a plain block.
        var withoutRole = await context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM PlannedBlocks WHERE ContractId IS NOT NULL AND ContractRole IS NULL")
            .SingleAsync();
        withoutRole.Should().Be(0);
    }

    [Fact]
    public async Task Migration_RoundsFractionalRedemptionEntriesUpToWholeMinutes()
    {
        // Gap 92 (V10): coverage is whole minutes, stored as a JSON integer. An older fraction is
        // rounded up, never down, so no stored coverage shrinks; a whole value stays as it was.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new NenFocusDbContext(
            new DbContextOptionsBuilder<NenFocusDbContext>().UseSqlite(connection).Options);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync("20261007130417_MoveRedemptionReceiptToFocusBlock");
        var redeeming = Guid.NewGuid();
        var firstMissed = Guid.NewGuid();
        var secondMissed = Guid.NewGuid();
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO PlannedBlocks (Id, BlockKind, BlockPresetId, Start, \"End\", ContractId, ContractRole, RedemptionEntries) " +
            "VALUES ({0}, 'Focus', {1}, '2026-01-02 10:00:00', '2026-01-02 11:00:00', {2}, 'Redemption', {3});",
            redeeming, Guid.NewGuid(), Guid.NewGuid(),
            $"[{{\"MissedBlockId\":\"{firstMissed}\",\"MinutesCovered\":31.9}},{{\"MissedBlockId\":\"{secondMissed}\",\"MinutesCovered\":28}}]");

        await migrator.MigrateAsync();

        var receipt = ((FocusBlock)await context.PlannedBlocks.AsNoTracking().SingleAsync(b => b.Id == redeeming)).Receipt;
        receipt.CoveredFor(firstMissed).Should().Be(WholeMinutes.Of(32));
        receipt.CoveredFor(secondMissed).Should().Be(WholeMinutes.Of(28));
    }
}
