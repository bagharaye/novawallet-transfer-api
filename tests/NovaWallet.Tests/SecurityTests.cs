using System.Net;
using System.Net.Http.Json;
using NovaWallet.Tests.Fixtures;
using NovaWallet.Tests.Helpers;
using Xunit;

namespace NovaWallet.Tests;

/// <summary>
/// A competent first pass at authn/authz bypass and obvious injection, per the
/// brief — not a full pen-test. TEST_STRATEGY.md §2 step 6 and §3 spell out what's
/// deliberately not covered here (token lifecycle, fuzzing, timing attacks, etc.).
/// </summary>
public sealed class SecurityTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private readonly HttpClient _authed = factory.CreateAuthenticatedClient();

    public static IEnumerable<object[]> ProtectedRequests()
    {
        yield return [HttpMethod.Post, "/wallets", null!];
        yield return [HttpMethod.Get, "/wallets/anything", null!];
        yield return [HttpMethod.Post, "/wallets/anything/credit", new { amountKobo = 1_000 }];
        yield return [HttpMethod.Post, "/transfers", new { fromWalletId = "a", toWalletId = "b", amountKobo = 1_000 }];
    }

    [Theory]
    [MemberData(nameof(ProtectedRequests))]
    public async Task Endpoint_WithoutAuthorizationHeader_Returns401(HttpMethod method, string path, object? body)
    {
        var client = factory.CreateClient(); // no Authorization header at all

        var response = await SendAsync(client, method, path, body, authHeader: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedRequests))]
    public async Task Endpoint_WithWrongBearerToken_Returns401(HttpMethod method, string path, object? body)
    {
        var client = factory.CreateClient();

        var response = await SendAsync(client, method, path, body, authHeader: "Bearer not-the-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("novawallet-dev-token")] // valid token, no "Bearer " scheme prefix
    [InlineData("")]
    public async Task CreateWallet_WithMalformedAuthorizationHeader_Returns401(string rawHeaderValue)
    {
        var client = factory.CreateClient();
        if (!string.IsNullOrEmpty(rawHeaderValue))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", rawHeaderValue);
        }

        var response = await client.PostAsync("/wallets", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidToken_CanReadAnyWallet_DocumentedSingleTokenLimitation()
    {
        // Part A implements one shared bearer token for the whole API (per the brief:
        // "a hardcoded/mock token is fine"), not per-wallet ownership or per-user
        // scoping. So a valid token legitimately can read/credit/transfer any wallet
        // ID it knows — there is no "another customer's wallet" concept to bypass in
        // this reference implementation. This test documents that as known, intended
        // scope for Part A rather than treating it as a silently-assumed bypass; see
        // TEST_STRATEGY.md §3 for why per-wallet authorization is out of scope here.
        var walletCreatedByOneClient = await _authed.CreateWalletAsync();

        var otherClientWithSameSharedToken = factory.CreateAuthenticatedClient();
        var wallet = await otherClientWithSameSharedToken.GetWalletAsync(walletCreatedByOneClient.Id);

        Assert.Equal(walletCreatedByOneClient.Id, wallet.Id);
    }

    [Theory]
    [InlineData("' OR '1'='1")]
    [InlineData("'; DROP TABLE wallets; --")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("../../etc/passwd")]
    public async Task GetWallet_InjectionLikeId_TreatedAsUnknownWallet_NoServerError(string maliciousId)
    {
        var response = await _authed.GetAsync($"/wallets/{Uri.EscapeDataString(maliciousId)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_InjectionLikeWalletIds_TreatedAsUnknownWallet_NoServerError()
    {
        var response = await _authed.TransferRawAsync("'; DROP TABLE wallets; --", "<script>alert(1)</script>", 1_000);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_OversizedWalletId_DoesNotCauseServerError()
    {
        // The oversized value goes in the JSON body (not a URL path segment) — an
        // oversized URL segment would be rejected by the HTTP client itself before
        // any request reaches the server, which would test the client library, not
        // the API's own handling of unexpectedly large input.
        var oversizedId = new string('a', 100_000);

        var response = await _authed.TransferRawAsync(oversizedId, "some-other-wallet", 1_000);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body, string? authHeader)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (authHeader is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authHeader);
        }

        return client.SendAsync(request);
    }
}
