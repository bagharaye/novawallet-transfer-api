namespace NovaWallet.Api.Options;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public string BearerToken { get; set; } = "novawallet-dev-token";

    public long DailyOutboundLimitKobo { get; set; } = 50_000_000; // NGN 500,000.00
}
