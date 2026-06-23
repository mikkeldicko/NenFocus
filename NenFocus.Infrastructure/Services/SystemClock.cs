using NenFocus.Application.Interfaces;

namespace NenFocus.Infrastructure.Services;

public sealed class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
