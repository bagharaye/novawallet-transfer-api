using System.Net;
using System.Net.Http.Json;
using System.Text;
using NovaWallet.Tests.Fixtures;
using NovaWallet.Tests.Helpers;
using Xunit;

namespace NovaWallet.Tests;

/// <summary>Happy-path, boundary, and negative cases for every endpoint — TEST_STRATEGY.md §2 step 1.</summary>
public sealed class FunctionalTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    [Fact]
    public async Task Health_RequiresNoToken_Returns200()
    {
        // Deployment platforms (Render, etc.) health-check without a bearer token;
        // this must stay reachable without one, and must reveal no wallet data.
        var unauthed = factory.CreateClient();

        var response = await unauthed.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateWallet_StartsAtZeroBalance()
    {
        var wallet = await _client.CreateWalletAsync();

        Assert.Equal(0, wallet.BalanceKobo);
        Assert.False(string.IsNullOrWhiteSpace(wallet.Id));
    }

    [Fact]
    public async Task GetWallet_UnknownId_Returns404()
    {
        var response = await _client.GetAsync("/wallets/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.ReadErrorAsync();
        Assert.Equal("wallet_not_found", error!.Error);
    }

    [Fact]
    public async Task Credit_IncreasesBalance()
    {
        var wallet = await _client.CreateWalletAsync();

        var credited = await _client.CreditWalletAsync(wallet.Id, 25_000);

        Assert.Equal(25_000, credited.BalanceKobo);
    }

    [Fact]
    public async Task Credit_UnknownWallet_Returns404()
    {
        var response = await _client.CreditRawAsync("does-not-exist", new { amountKobo = 1_000 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1_000_000)]
    public async Task Credit_NonPositiveAmount_Returns400(long amountKobo)
    {
        var wallet = await _client.CreateWalletAsync();

        var response = await _client.CreditRawAsync(wallet.Id, new { amountKobo });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unchanged = await _client.GetWalletAsync(wallet.Id);
        Assert.Equal(0, unchanged.BalanceKobo);
    }

    [Fact]
    public async Task Credit_MissingAmountField_Returns400()
    {
        var wallet = await _client.CreateWalletAsync();

        var response = await _client.PostAsJsonAsync($"/wallets/{wallet.Id}/credit", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Credit_SyntacticallyInvalidJson_Returns400WithErrorBody()
    {
        // Regression test for bug-reports/03: genuinely malformed JSON (not just a
        // wrong-typed field) used to be rejected with an empty 400 body by ASP.NET
        // Core's own model binder, bypassing the API's error contract entirely.
        var wallet = await _client.CreateWalletAsync();
        var content = new StringContent("{amountKobo: 100", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/wallets/{wallet.Id}/credit", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadErrorAsync();
        Assert.Equal("validation_error", error!.Error);
    }

    [Fact]
    public async Task Credit_EmptyBody_Returns400WithErrorBody()
    {
        var wallet = await _client.CreateWalletAsync();
        var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/wallets/{wallet.Id}/credit", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadErrorAsync();
        Assert.Equal("validation_error", error!.Error);
    }

    [Fact]
    public async Task Credit_SmallestPositiveUnit_OneKobo_Accepted()
    {
        var wallet = await _client.CreateWalletAsync();

        var credited = await _client.CreditWalletAsync(wallet.Id, 1);

        Assert.Equal(1, credited.BalanceKobo);
    }

    [Fact]
    public async Task Transfer_HappyPath_MovesFundsBetweenWallets()
    {
        var from = await _client.CreateFundedWalletAsync(100_000);
        var to = await _client.CreateWalletAsync();

        var transfer = await _client.TransferAsync(from.Id, to.Id, 40_000);

        Assert.Equal(40_000, transfer.AmountKobo);
        Assert.Equal(60_000, (await _client.GetWalletAsync(from.Id)).BalanceKobo);
        Assert.Equal(40_000, (await _client.GetWalletAsync(to.Id)).BalanceKobo);
    }

    [Fact]
    public async Task Transfer_SameWalletBothSides_Returns400()
    {
        var wallet = await _client.CreateFundedWalletAsync(10_000);

        var response = await _client.TransferRawAsync(wallet.Id, wallet.Id, 1_000);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10_000, (await _client.GetWalletAsync(wallet.Id)).BalanceKobo);
    }

    [Fact]
    public async Task Transfer_UnknownFromWallet_Returns404()
    {
        var to = await _client.CreateWalletAsync();

        var response = await _client.TransferRawAsync("does-not-exist", to.Id, 1_000);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_UnknownToWallet_Returns404()
    {
        var from = await _client.CreateFundedWalletAsync(10_000);

        var response = await _client.TransferRawAsync(from.Id, "does-not-exist", 1_000);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(10_000, (await _client.GetWalletAsync(from.Id)).BalanceKobo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public async Task Transfer_NonPositiveAmount_Returns400(long amountKobo)
    {
        var from = await _client.CreateFundedWalletAsync(10_000);
        var to = await _client.CreateWalletAsync();

        var response = await _client.TransferRawAsync(from.Id, to.Id, amountKobo);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_ExceedsBalance_Returns422AndLeavesBalancesUnchanged()
    {
        var from = await _client.CreateFundedWalletAsync(5_000);
        var to = await _client.CreateWalletAsync();

        var response = await _client.TransferRawAsync(from.Id, to.Id, 5_001);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.ReadErrorAsync();
        Assert.Equal("insufficient_funds", error!.Error);
        Assert.Equal(5_000, (await _client.GetWalletAsync(from.Id)).BalanceKobo);
        Assert.Equal(0, (await _client.GetWalletAsync(to.Id)).BalanceKobo);
    }

    [Fact]
    public async Task Transfer_TransfersEntireBalance_LeavesZero()
    {
        var from = await _client.CreateFundedWalletAsync(5_000);
        var to = await _client.CreateWalletAsync();

        var response = await _client.TransferRawAsync(from.Id, to.Id, 5_000);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0, (await _client.GetWalletAsync(from.Id)).BalanceKobo);
        Assert.Equal(5_000, (await _client.GetWalletAsync(to.Id)).BalanceKobo);
    }

    [Fact]
    public async Task Transfer_MissingFromWalletId_Returns400()
    {
        var to = await _client.CreateWalletAsync();

        var response = await _client.TransferRawAsync(null, to.Id, 1_000);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_MissingToWalletId_Returns400()
    {
        var from = await _client.CreateFundedWalletAsync(10_000);

        var response = await _client.TransferRawAsync(from.Id, null, 1_000);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
