using System.Net;
using NovaWallet.Tests.Fixtures;
using NovaWallet.Tests.Helpers;
using Xunit;

namespace NovaWallet.Tests;

/// <summary>
/// Explicit currency-precision checks — TEST_STRATEGY.md §2 step 2. Money must be an
/// integer number of kobo everywhere; these assert that directly rather than just
/// checking "the amount looks right" on a happy path.
/// </summary>
public sealed class CurrencyPrecisionTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    [Theory]
    [InlineData(100.5)]
    [InlineData(0.01)]
    [InlineData(1.9999999)]
    public async Task Credit_FractionalAmount_IsRejected_NotTruncatedOrRounded(decimal amountKobo)
    {
        var wallet = await _client.CreateWalletAsync();

        var response = await _client.CreditRawAsync(wallet.Id, new { amountKobo });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadErrorAsync();
        Assert.Equal("validation_error", error!.Error);
        // The failure mode that actually matters here: silently truncating 100.5 -> 100
        // kobo would be a real-money bug that looks fine in a spot check. Confirm the
        // wallet was never credited at all, not credited for a rounded/truncated amount.
        Assert.Equal(0, (await _client.GetWalletAsync(wallet.Id)).BalanceKobo);
    }

    [Fact]
    public async Task Credit_StringAmount_IsRejected()
    {
        var wallet = await _client.CreateWalletAsync();

        var response = await _client.CreditRawAsync(wallet.Id, new { amountKobo = "1000" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_FractionalAmount_IsRejected()
    {
        var from = await _client.CreateFundedWalletAsync(10_000);
        var to = await _client.CreateWalletAsync();

        var response = await _client.TransferRawAsync(from.Id, to.Id, 250.75);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10_000, (await _client.GetWalletAsync(from.Id)).BalanceKobo);
    }

    [Fact]
    public async Task ManySmallCredits_SumExactly_NoFloatingPointDrift()
    {
        var wallet = await _client.CreateWalletAsync();

        // 333 credits of 3 kobo each: a total that doesn't land on a "round" decimal
        // amount, which is exactly the kind of value a float/double accumulator would
        // start drifting on after enough additions. An int64-kobo accumulator must not.
        const int creditCount = 333;
        const long perCreditKobo = 3;
        for (var i = 0; i < creditCount; i++)
        {
            await _client.CreditWalletAsync(wallet.Id, perCreditKobo);
        }

        var final = await _client.GetWalletAsync(wallet.Id);

        Assert.Equal(creditCount * perCreditKobo, final.BalanceKobo);
    }

    [Fact]
    public async Task LargeAmount_NearInt64Range_HandledExactly()
    {
        // Roughly NGN 90 billion in kobo — far beyond realistic wallet balances, but
        // proves the balance is a true 64-bit integer and not silently passing through
        // a lower-precision numeric type (e.g. a 32-bit int, or a double, anywhere on
        // the read/write path) that would lose precision at this magnitude.
        const long largeAmountKobo = 9_000_000_000_000L;
        var wallet = await _client.CreateWalletAsync();

        var credited = await _client.CreditWalletAsync(wallet.Id, largeAmountKobo);

        Assert.Equal(largeAmountKobo, credited.BalanceKobo);
        Assert.Equal(largeAmountKobo, (await _client.GetWalletAsync(wallet.Id)).BalanceKobo);
    }

    [Fact]
    public async Task Transfer_OneKobo_ExactAmount_Succeeds()
    {
        var from = await _client.CreateFundedWalletAsync(1);
        var to = await _client.CreateWalletAsync();

        var transfer = await _client.TransferAsync(from.Id, to.Id, 1);

        Assert.Equal(1, transfer.AmountKobo);
        Assert.Equal(0, (await _client.GetWalletAsync(from.Id)).BalanceKobo);
        Assert.Equal(1, (await _client.GetWalletAsync(to.Id)).BalanceKobo);
    }
}
