using System.Net;
using NovaWallet.Tests.Fixtures;
using NovaWallet.Tests.Helpers;
using Xunit;

namespace NovaWallet.Tests;

/// <summary>Idempotency-Key replay behavior — TEST_STRATEGY.md §2 step 3.</summary>
public sealed class IdempotencyTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    [Fact]
    public async Task ReplaySameKeySamePayload_ReturnsIdenticalResponse_AndChargesOnlyOnce()
    {
        var from = await _client.CreateFundedWalletAsync(100_000);
        var to = await _client.CreateWalletAsync();
        var key = $"idem-{Guid.NewGuid():N}";

        var first = await _client.TransferAsync(from.Id, to.Id, 40_000, key);
        var second = await _client.TransferAsync(from.Id, to.Id, 40_000, key);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.Equal(60_000, (await _client.GetWalletAsync(from.Id)).BalanceKobo);
        Assert.Equal(40_000, (await _client.GetWalletAsync(to.Id)).BalanceKobo);
    }

    [Fact]
    public async Task ReplaySameKeyDifferentPayload_Returns409_OriginalTransferUnaffected()
    {
        var from = await _client.CreateFundedWalletAsync(100_000);
        var to = await _client.CreateWalletAsync();
        var otherTo = await _client.CreateWalletAsync();
        var key = $"idem-{Guid.NewGuid():N}";

        var original = await _client.TransferAsync(from.Id, to.Id, 40_000, key);

        var conflict = await _client.TransferRawAsync(from.Id, otherTo.Id, 40_000, key);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var error = await conflict.ReadErrorAsync();
        Assert.Equal("idempotency_key_conflict", error!.Error);

        // The original transfer's effect must stand untouched, and the conflicting
        // replay must not have moved any additional money anywhere.
        Assert.Equal(60_000, (await _client.GetWalletAsync(from.Id)).BalanceKobo);
        Assert.Equal(40_000, (await _client.GetWalletAsync(to.Id)).BalanceKobo);
        Assert.Equal(0, (await _client.GetWalletAsync(otherTo.Id)).BalanceKobo);
        Assert.NotEqual(default, original.CreatedAt);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(2_000)]
    [InlineData(3_000)]
    public async Task DifferentKeys_AreProcessedIndependently(long amountKobo)
    {
        var from = await _client.CreateFundedWalletAsync(100_000);
        var to = await _client.CreateWalletAsync();

        var a = await _client.TransferAsync(from.Id, to.Id, amountKobo, $"k-{Guid.NewGuid():N}");
        var b = await _client.TransferAsync(from.Id, to.Id, amountKobo, $"k-{Guid.NewGuid():N}");

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2 * amountKobo, (await _client.GetWalletAsync(to.Id)).BalanceKobo);
    }

    [Fact]
    public async Task NoIdempotencyKey_EachRequestIsProcessedAsANewTransfer()
    {
        // Documented assumption (TEST_STRATEGY.md §4): a transfer without an
        // Idempotency-Key header is treated as a one-off, non-replayable request —
        // each call creates a new transfer, since there's no key to dedupe against.
        var from = await _client.CreateFundedWalletAsync(100_000);
        var to = await _client.CreateWalletAsync();

        var first = await _client.TransferAsync(from.Id, to.Id, 10_000);
        var second = await _client.TransferAsync(from.Id, to.Id, 10_000);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(20_000, (await _client.GetWalletAsync(to.Id)).BalanceKobo);
    }
}
