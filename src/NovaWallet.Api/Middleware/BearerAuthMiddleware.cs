using System.Text.Json;
using Microsoft.Extensions.Options;
using NovaWallet.Api.Models;
using NovaWallet.Api.Options;

namespace NovaWallet.Api.Middleware;

/// <summary>
/// A basic bearer-token check applied to every endpoint, per the brief:
/// "a hardcoded/mock token is fine." Not a real auth server — no expiry,
/// no scopes, no per-wallet ownership. See TEST_STRATEGY.md for what that
/// implies is (and isn't) covered by the security test pass.
/// </summary>
public sealed class BearerAuthMiddleware(RequestDelegate next, IOptions<ApiOptions> apiOptions)
{
    private static readonly string[] ExemptPathPrefixes = ["/swagger", "/openapi"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _expectedHeaderValue = $"Bearer {apiOptions.Value.BearerToken}";

    public async Task InvokeAsync(HttpContext context)
    {
        if (ExemptPathPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)))
        {
            await next(context);
            return;
        }

        var providedHeader = context.Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(providedHeader) ||
            !string.Equals(providedHeader, _expectedHeaderValue, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(
                new ErrorResponse("unauthorized", "Missing or invalid bearer token."), JsonOptions));
            return;
        }

        await next(context);
    }
}
