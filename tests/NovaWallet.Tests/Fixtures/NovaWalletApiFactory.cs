using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovaWallet.Api.Services;

namespace NovaWallet.Tests.Fixtures;

/// <summary>
/// Boots the real Part A app in-process (same DI container, same middleware
/// pipeline, same in-memory <see cref="WalletStore"/>) so tests exercise
/// actual HTTP behavior rather than calling service classes directly. The
/// only substitution is <see cref="IClock"/>, swapped for a settable
/// <see cref="TestClock"/> so the WAT daily-limit reset can be tested at its
/// exact boundary without sleeping until real midnight.
/// </summary>
public sealed class NovaWalletApiFactory : WebApplicationFactory<Program>
{
    public const string BearerToken = "novawallet-dev-token";
    public const long DailyOutboundLimitKobo = 50_000_000;

    public TestClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", BearerToken);
        return client;
    }
}
