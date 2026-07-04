using FluentAssertions;
using NenFocus.Application.Interfaces;
using NenFocus.Application.Queries.ComputeActivePolicySlice;
using NenFocus.Domain.Entities;
using NenFocus.Domain.Enums;
using NenFocus.Domain.Services;
using NenFocus.Domain.ValueObjects;
using NSubstitute;

namespace NenFocus.Tests.Application.Queries;

public class ComputeActivePolicySliceQueryHandlerTests
{
    private static readonly DateTime At = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IPlannedBlockRepository _blocks = Substitute.For<IPlannedBlockRepository>();
    private readonly IBlockProfileRepository _profiles = Substitute.For<IBlockProfileRepository>();
    private readonly ISessionRepository _sessions = Substitute.For<ISessionRepository>();
    private readonly IPolicyEngine _policyEngine = Substitute.For<IPolicyEngine>();

    public ComputeActivePolicySliceQueryHandlerTests()
    {
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[]);
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[]);
    }

    private ComputeActivePolicySliceQueryHandler CreateHandler() => new(_blocks, _profiles, _sessions, _policyEngine);

    [Fact]
    public async Task HandleAsync_FetchesBlocksForOneSecondWindowAroundAt()
    {
        await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(At), CancellationToken.None);

        await _blocks.Received(1).GetForRangeAsync(At.AddSeconds(-1), At.AddSeconds(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ReturnsWhateverThePolicyEngineComputes()
    {
        var expectedSlice = new ActivePolicySlice { SliceStart = At, ActiveBlockType = BlockType.Focus };
        _policyEngine.ComputeSlice(At, Arg.Any<IEnumerable<BlockProfile>>(), Arg.Any<IEnumerable<Session>>(), Arg.Any<IEnumerable<PlannedBlock>>())
            .Returns(expectedSlice);

        var result = await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(At), CancellationToken.None);

        result.Should().BeSameAs(expectedSlice);
    }

    [Fact]
    public async Task HandleAsync_ResolvesDistinctProfilesReferencedByBlocksAndSessions()
    {
        var sharedProfileId = Guid.NewGuid();
        var sessionOnlyProfileId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), sharedProfileId, At, At.AddHours(1));
        var session = Session.Create(
            Guid.NewGuid(), new SessionGoal("Goal"), new SessionDuration(TimeSpan.FromMinutes(30)),
            sessionOnlyProfileId, SessionOrigin.Spontaneous);
        session.Start(At);

        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);
        _sessions.GetActiveSessionsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Session>)[session]);

        var sharedProfile = BlockProfile.Create(sharedProfileId, "Shared", WebsitePolicy.Blocklist, BlockType.Focus, At);
        var sessionProfile = BlockProfile.Create(sessionOnlyProfileId, "SessionOnly", WebsitePolicy.Blocklist, BlockType.Focus, At);
        _profiles.GetByIdAsync(sharedProfileId, Arg.Any<CancellationToken>()).Returns(sharedProfile);
        _profiles.GetByIdAsync(sessionOnlyProfileId, Arg.Any<CancellationToken>()).Returns(sessionProfile);

        await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(At), CancellationToken.None);

        _policyEngine.Received(1).ComputeSlice(
            At,
            Arg.Is<IEnumerable<BlockProfile>>(p => p.Count() == 2 && p.Contains(sharedProfile) && p.Contains(sessionProfile)),
            Arg.Is<IEnumerable<Session>>(s => s.Contains(session)),
            Arg.Is<IEnumerable<PlannedBlock>>(b => b.Contains(block)));
    }

    [Fact]
    public async Task HandleAsync_UnresolvableProfile_IsOmittedNotThrown()
    {
        var profileId = Guid.NewGuid();
        var block = FocusBlock.Create(Guid.NewGuid(), profileId, At, At.AddHours(1));
        _blocks.GetForRangeAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PlannedBlock>)[block]);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns((BlockProfile?)null);

        await CreateHandler().HandleAsync(new ComputeActivePolicySliceQuery(At), CancellationToken.None);

        _policyEngine.Received(1).ComputeSlice(
            At, Arg.Is<IEnumerable<BlockProfile>>(p => !p.Any()), Arg.Any<IEnumerable<Session>>(), Arg.Any<IEnumerable<PlannedBlock>>());
    }
}
