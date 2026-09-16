using System.Net;
using NovaWallet.Tests.Fixtures;
using NovaWallet.Tests.Helpers;
using Xunit;

namespace NovaWallet.Tests;

/// <summary>
/// Daily outbound limit enforcement and its WAT-midnight reset —
/// TEST_STRATEGY.md §2 step 5. The WAT-vs-UTC reset boundary is the specific
/// edge case the brief calls out as easy to get wrong, so it gets a dedicated,
/// precisely-timed test rather than being folded into the boundary checks.
/// </summary>
public sealed class DailyLimitTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private const long LimitKobo = NovaWalletApiFactory.DailyOutboundLimitKobo;
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    [Fact]
    public async Task Transfer_ExactlyAtDailyLimit_Succeeds()
    {
        factory.Clock.Set(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        var from = await _client.CreateFundedWalletAsync(LimitKobo + 1_000);
        var to = await _client.CreateWalletAsync();

        var response = await _client.TransferRawAsync(from.Id, to.Id, LimitKobo);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_OneKoboOverDailyLimit_RejectedAsLimitExceeded_NotInsufficientFunds()
    {
        factory.Clock.Set(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        // Balance is comfortably above the limit, so a rejection here can only be the
        // daily-limit check, not a funds check — isolates which rule actually fired.
        var from = await _client.CreateFundedWalletAsync(LimitKobo + 1_000);
        var to = await _client.CreateWalletAsync();

        var response = await _client.TransferRawAsync(from.Id, to.Id, LimitKobo + 1);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.ReadErrorAsync();
        Assert.Equal("daily_limit_exceeded", error!.Error);
    }

    [Fact]
    public async Task MultipleTransfers_CumulativeAmountExceedingLimit_SecondIsRejected_ThirdSucceedsAtExactRemainder()
    {
        factory.Clock.Set(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        var from = await _client.CreateFundedWalletAsync(LimitKobo + 1_000);
        var to = await _client.CreateWalletAsync();

        var firstResponse = await _client.TransferRawAsync(from.Id, to.Id, LimitKobo - 100);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // Only 100 kobo of today's budget remains; this would push cumulative usage
        // 100 kobo over the limit even though the wallet has more than enough balance.
        var secondResponse = await _client.TransferRawAsync(from.Id, to.Id, 200);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, secondResponse.StatusCode);

        var thirdResponse = await _client.TransferRawAsync(from.Id, to.Id, 100);
        Assert.Equal(HttpStatusCode.Created, thirdResponse.StatusCode);
    }

    [Fact]
    public async Task DailyLimit_ResetsAtWatMidnight_AnHourBeforeTheUtcCalendarDateRolls()
    {
        var from = await _client.CreateFundedWalletAsync(2 * LimitKobo);
        var to = await _client.CreateWalletAsync();

        // 2026-01-01 12:00 UTC = 2026-01-01 13:00 WAT — safely mid-day, WAT day = Jan 1.
        factory.Clock.Set(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var spend = await _client.TransferRawAsync(from.Id, to.Id, LimitKobo - 100);
        Assert.Equal(HttpStatusCode.Created, spend.StatusCode);
        // 100 kobo of the Jan-1-WAT budget remains.

        // 2026-01-01 22:59:59 UTC = 2026-01-01 23:59:59 WAT — one second before WAT
        // midnight. Still the same WAT calendar day (and the same UTC calendar day),
        // so the remaining budget must still be just 100 kobo.
        factory.Clock.Set(new DateTimeOffset(2026, 1, 1, 22, 59, 59, TimeSpan.Zero));
        var stillJan1Wat = await _client.TransferRawAsync(from.Id, to.Id, 200);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, stillJan1Wat.StatusCode);

        // 2026-01-01 23:00:00 UTC = 2026-01-02 00:00:00 WAT — exactly WAT midnight.
        // Critically, the UTC calendar date is still January 1 (UTC midnight is a full
        // hour away) — a server that reset on UTC midnight, or on its own host-local
        // timezone, would still refuse this. Resetting on WAT midnight allows it.
        factory.Clock.Set(new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.Zero));
        var nowJan2Wat = await _client.TransferRawAsync(from.Id, to.Id, 200);
        Assert.Equal(HttpStatusCode.Created, nowJan2Wat.StatusCode);
    }
}
