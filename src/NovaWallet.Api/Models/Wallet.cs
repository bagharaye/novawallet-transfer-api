namespace NovaWallet.Api.Models;

public sealed class Wallet
{
    public required string Id { get; init; }
    public long BalanceKobo { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Sum of successful outbound transfers for <see cref="DailyLimitWatDate"/> so far.</summary>
    public long DailyOutboundUsedKobo { get; set; }

    /// <summary>The WAT calendar date <see cref="DailyOutboundUsedKobo"/> is being accumulated for.</summary>
    public DateOnly DailyLimitWatDate { get; set; }
}
