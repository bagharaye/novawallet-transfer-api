using NovaWallet.Api.Services;

namespace NovaWallet.Tests.Fixtures;

/// <summary>
/// A settable clock so tests can move time across the WAT midnight boundary
/// deterministically instead of sleeping until real midnight. Defaults to the
/// real current time so tests that don't care about the daily-limit reset
/// behave normally.
/// </summary>
public sealed class TestClock : IClock
{
    private readonly object _lock = new();
    private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

    public DateTimeOffset UtcNow
    {
        get { lock (_lock) return _utcNow; }
    }

    public void Set(DateTimeOffset value)
    {
        lock (_lock) _utcNow = value;
    }
}
