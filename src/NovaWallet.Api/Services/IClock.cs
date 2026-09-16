namespace NovaWallet.Api.Services;

/// <summary>
/// Abstraction over "now" so tests can move time without sleeping and without
/// depending on the host machine's local timezone.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
