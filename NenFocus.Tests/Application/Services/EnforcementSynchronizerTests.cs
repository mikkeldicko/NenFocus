using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeActivePolicySlice;
using NenFocus.Application.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Services;

public class EnforcementSynchronizerTests
{
    private readonly IQueryDispatcher _queries = Substitute.For<IQueryDispatcher>();
    private readonly IEnforcementService _enforcement = Substitute.For<IEnforcementService>();

    private EnforcementSynchronizer CreateSynchronizer() => new(_queries, _enforcement);

    private static ActivePolicySlice NonEmptySlice() =>
        new() { BlockedSites = new HashSet<string> { "youtube.com" } };

    private void SliceIs(ActivePolicySlice slice) =>
        _queries.QueryAsync(Arg.Any<ComputeActivePolicySliceQuery>(), Arg.Any<CancellationToken>())
            .Returns(slice);

    [Fact]
    public async Task SyncAsync_NonEmptySlice_AppliesIt()
    {
        var slice = NonEmptySlice();
        SliceIs(slice);

        await CreateSynchronizer().SyncAsync(At.Utc(DateTime.UtcNow));

        await _enforcement.Received(1).ApplyPolicyAsync(slice, Arg.Any<CancellationToken>());
        await _enforcement.DidNotReceive().RemoveEnforcementAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAsync_EmptySlice_RemovesEnforcement()
    {
        SliceIs(new ActivePolicySlice());

        await CreateSynchronizer().SyncAsync(At.Utc(DateTime.UtcNow));

        await _enforcement.Received(1).RemoveEnforcementAsync(Arg.Any<CancellationToken>());
        await _enforcement.DidNotReceive().ApplyPolicyAsync(Arg.Any<ActivePolicySlice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAsync_ComputesSliceAtTheGivenInstant()
    {
        var at = new DateTime(2026, 9, 25, 10, 25, 0, DateTimeKind.Utc);
        SliceIs(new ActivePolicySlice());

        await CreateSynchronizer().SyncAsync(At.Utc(at));

        await _queries.Received(1).QueryAsync(
            Arg.Is<ComputeActivePolicySliceQuery>(q => q.At == At.Utc(at)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAsync_ReturnsTheEnforcedSlice()
    {
        var slice = NonEmptySlice();
        SliceIs(slice);

        var result = await CreateSynchronizer().SyncAsync(At.Utc(DateTime.UtcNow));

        result.Should().BeSameAs(slice);
    }

    [Fact]
    public async Task SyncAsync_ConcurrentCalls_SecondDoesNotComputeUntilFirstHasApplied()
    {
        // The race this guards against: the heartbeat computes a slice just before a break
        // is saved, the break's handler then applies the fresh break-time slice, and the
        // heartbeat finally applies its stale one on top. Compute + apply must be one unit.
        var firstApplyGate = new TaskCompletionSource();
        var applyCalls = 0;
        _enforcement.ApplyPolicyAsync(Arg.Any<ActivePolicySlice>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++applyCalls == 1 ? firstApplyGate.Task : Task.CompletedTask);
        SliceIs(NonEmptySlice());

        var synchronizer = CreateSynchronizer();
        var first = synchronizer.SyncAsync(At.Utc(DateTime.UtcNow));
        var second = synchronizer.SyncAsync(At.Utc(DateTime.UtcNow));

        await _queries.Received(1).QueryAsync(Arg.Any<ComputeActivePolicySliceQuery>(), Arg.Any<CancellationToken>());

        firstApplyGate.SetResult();
        await Task.WhenAll(first, second);

        await _queries.Received(2).QueryAsync(Arg.Any<ComputeActivePolicySliceQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAsync_FailedCall_ReleasesTheLockForTheNextCall()
    {
        _queries.QueryAsync(Arg.Any<ComputeActivePolicySliceQuery>(), Arg.Any<CancellationToken>())
            .Returns<ActivePolicySlice>(_ => throw new InvalidOperationException("db down"), _ => new ActivePolicySlice());

        var synchronizer = CreateSynchronizer();
        var failed = () => synchronizer.SyncAsync(At.Utc(DateTime.UtcNow));
        await failed.Should().ThrowAsync<InvalidOperationException>();

        var next = synchronizer.SyncAsync(At.Utc(DateTime.UtcNow));
        (await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(next);
    }
}
