using System.Net.Http.Json;
using NovaWallet.Api.Models;

namespace NovaWallet.Tests.Helpers;

/// <summary>
/// Thin request helpers so individual tests read as intent ("create a funded
/// wallet", "transfer X") instead of repeating HTTP/JSON plumbing. This is
/// the suite's "factory" for test data — wallets are always created through
/// the real API, never hardcoded — per the take-home's stretch goal for test
/// data management.
/// </summary>
public static class WalletApiClientExtensions
{
    public static async Task<WalletResponse> CreateWalletAsync(this HttpClient client)
    {
        var response = await client.PostAsync("/wallets", content: null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WalletResponse>())!;
    }

    /// <summary>Creates a wallet and immediately credits it — the common setup for transfer tests.</summary>
    public static async Task<WalletResponse> CreateFundedWalletAsync(this HttpClient client, long amountKobo)
    {
        var wallet = await client.CreateWalletAsync();
        return await client.CreditWalletAsync(wallet.Id, amountKobo);
    }

    public static Task<HttpResponseMessage> CreditRawAsync(this HttpClient client, string walletId, object body) =>
        client.PostAsJsonAsync($"/wallets/{walletId}/credit", body);

    public static async Task<WalletResponse> CreditWalletAsync(this HttpClient client, string walletId, long amountKobo)
    {
        var response = await client.CreditRawAsync(walletId, new { amountKobo });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WalletResponse>())!;
    }

    public static Task<HttpResponseMessage> TransferRawAsync(
        this HttpClient client, object? fromWalletId, object? toWalletId, object? amountKobo, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/transfers")
        {
            Content = JsonContent.Create(new { fromWalletId, toWalletId, amountKobo }),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request);
    }

    public static async Task<TransferResponse> TransferAsync(
        this HttpClient client, string fromWalletId, string toWalletId, long amountKobo, string? idempotencyKey = null)
    {
        var response = await client.TransferRawAsync(fromWalletId, toWalletId, amountKobo, idempotencyKey);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransferResponse>())!;
    }

    public static async Task<WalletResponse> GetWalletAsync(this HttpClient client, string walletId)
    {
        var response = await client.GetAsync($"/wallets/{walletId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WalletResponse>())!;
    }

    public static async Task<ErrorResponse?> ReadErrorAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<ErrorResponse>();
}
