using System.Net;
using System.Net.Http.Json;
using NovaWallet.Api.Models;
using NovaWallet.Tests.Fixtures;
using NovaWallet.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace NovaWallet.Tests;

/// <summary>
/// Fires genuinely concurrent requests and inspects the actual outcome —
/// TEST_STRATEGY.md §2 step 4. This is evidence, not assertion-by-construction:
/// every test here reports the observed status-code distribution and final
/// balance as part of its failure message, so a failure is diagnosable from
/// the test output alone.
/// </summary>
public sealed class ConcurrencyTests(NovaWalletApiFactory factory, ITestOutputHelper output)
    : IClassFixture<NovaWalletApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    [Fact]
    public async Task ConcurrentTransfers_OnlyEnoughBalanceForOne_ExactlyOneSucceeds()
    {
        const int concurrentRequests = 50;
        const long balanceKobo = 10_000;

        var from = await _client.CreateFundedWalletAsync(balanceKobo);
        var to = await _client.CreateWalletAsync();

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => _client.TransferRawAsync(from.Id, to.Id, balanceKobo))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var rejected = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        var finalFrom = await _client.GetWalletAsync(from.Id);
        var finalTo = await _client.GetWalletAsync(to.Id);

        var evidence = $"succeeded={succeeded} rejected={rejected} " +
                        $"otherStatusCodes=[{string.Join(",", responses.Select(r => (int)r.StatusCode).Where(c => c != 201 && c != 422))}] " +
                        $"finalFromBalance={finalFrom.BalanceKobo} finalToBalance={finalTo.BalanceKobo}";
        output.WriteLine(evidence);

        Assert.True(finalFrom.BalanceKobo >= 0, $"Balance went negative — double-spend occurred. {evidence}");
        Assert.True(succeeded == 1, $"Expected exactly one of {concurrentRequests} concurrent requests to succeed. {evidence}");
        Assert.Equal(concurrentRequests - 1, rejected);
        Assert.Equal(0, finalFrom.BalanceKobo);
        Assert.Equal(balanceKobo, finalTo.BalanceKobo);
    }

    [Fact]
    public async Task ConcurrentTransfers_PartialCapacity_OnlyAsManySucceedAsBalanceAllows()
    {
        const int concurrentRequests = 20;
        const long perTransferKobo = 10_000;
        const long startingBalanceKobo = 100_000; // exactly enough for 10 of the 20 requests
        const int expectedSuccesses = (int)(startingBalanceKobo / perTransferKobo);

        var from = await _client.CreateFundedWalletAsync(startingBalanceKobo);
        var to = await _client.CreateWalletAsync();

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => _client.TransferRawAsync(from.Id, to.Id, perTransferKobo))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var finalFrom = await _client.GetWalletAsync(from.Id);
        var finalTo = await _client.GetWalletAsync(to.Id);

        var evidence = $"succeeded={succeeded} expected={expectedSuccesses} " +
                        $"finalFromBalance={finalFrom.BalanceKobo} finalToBalance={finalTo.BalanceKobo}";
        output.WriteLine(evidence);

        Assert.True(finalFrom.BalanceKobo >= 0, $"Balance went negative — double-spend occurred. {evidence}");
        Assert.True(succeeded == expectedSuccesses, $"Wrong number of transfers succeeded under contention. {evidence}");
        Assert.Equal(startingBalanceKobo - expectedSuccesses * perTransferKobo, finalFrom.BalanceKobo);
        Assert.Equal(expectedSuccesses * perTransferKobo, finalTo.BalanceKobo);
    }

    [Fact]
    public async Task ConcurrentReplay_SameIdempotencyKey_ProcessedExactlyOnce()
    {
        const int concurrentRequests = 30;
        var from = await _client.CreateFundedWalletAsync(50_000);
        var to = await _client.CreateWalletAsync();
        var key = $"concurrent-idem-{Guid.NewGuid():N}";

        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => _client.TransferRawAsync(from.Id, to.Id, 10_000, key))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<TransferResponse>()));
        var distinctTransferIds = bodies.Select(b => b!.Id).Distinct().ToArray();
        var finalTo = await _client.GetWalletAsync(to.Id);

        var evidence = $"distinctTransferIds={distinctTransferIds.Length} finalToBalance={finalTo.BalanceKobo} " +
                        $"statusCodes=[{string.Join(",", responses.Select(r => (int)r.StatusCode))}]";
        output.WriteLine(evidence);

        Assert.True(responses.All(r => r.StatusCode == HttpStatusCode.Created), $"All replays of an identical payload should succeed. {evidence}");
        Assert.True(distinctTransferIds.Length == 1, $"Concurrent replay of the same key+payload must be processed exactly once. {evidence}");
        Assert.Equal(10_000, finalTo.BalanceKobo);
    }
}
