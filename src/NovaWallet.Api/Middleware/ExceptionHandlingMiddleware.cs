using System.Text.Json;
using NovaWallet.Api.Errors;
using NovaWallet.Api.Models;

namespace NovaWallet.Api.Middleware;

/// <summary>
/// Central place that turns a thrown <see cref="ApiException"/> (or a
/// malformed-JSON body) into the API's one consistent error shape, so every
/// endpoint handler can just throw instead of repeating status-code plumbing.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException ex)
        {
            context.Response.StatusCode = (int)ex.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new ErrorResponse(ex.ErrorCode, ex.Message), JsonOptions));
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Malformed JSON body");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(
                new ErrorResponse("validation_error", "Request body is not valid JSON for this endpoint."),
                JsonOptions));
        }
    }
}
