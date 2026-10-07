using NenFocus.Application.DTOs;
using NenFocus.Application.Interfaces;

namespace NenFocus.Infrastructure.Services;

/// <summary>Interim home of the post-session and post-contract relays — see IRecapRelay (Gap 107).</summary>
public sealed class RecapRelay : IRecapRelay
{
    public event EventHandler<IReadOnlyList<PostSessionDto>>? PostSessionsReady;
    public event EventHandler? PostSessionAcknowledged;
    public event EventHandler<IReadOnlyList<NenContractRecapDto>>? PostContractsReady;

    public void NotifyPostSessionsReady(IReadOnlyList<PostSessionDto> postSessions)
    {
        if (postSessions.Count == 0) return;
        PostSessionsReady?.Invoke(this, postSessions);
    }

    public void NotifyPostSessionAcknowledged()
    {
        PostSessionAcknowledged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyPostContractsReady(IReadOnlyList<NenContractRecapDto> recaps)
    {
        if (recaps.Count == 0) return;
        PostContractsReady?.Invoke(this, recaps);
    }
}
