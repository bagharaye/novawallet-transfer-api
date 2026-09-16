using System.Text.Json;
using NovaWallet.Api.Errors;

namespace NovaWallet.Api.Models;

/// <summary>
/// Reads and deserializes the request body by hand instead of relying on minimal
/// API's implicit parameter binding. Implicit binding's own failure handling (bad
/// JSON syntax, or a missing/wrong Content-Type header) runs inside ASP.NET Core's
/// request-delegate machinery, *before* the endpoint handler or the app's own
/// ExceptionHandlingMiddleware ever gets a chance to run — it short-circuits with a
/// bare status code and an empty body, bypassing this API's error contract entirely
/// (see bug-reports/01 and bug-reports/03). Reading the body explicitly, inside the
/// handler, keeps every rejection on the same ValidationException path as everything
/// else, and deliberately doesn't require a specific Content-Type — the raw bytes are
/// what matter, not what header a client happened to send with them.
/// </summary>
public static class RequestBodyReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<T> ReadAsync<T>(HttpRequest request)
    {
        try
        {
            var value = await JsonSerializer.DeserializeAsync<T>(request.Body, JsonOptions);
            if (value is null)
            {
                throw new ValidationException("Request body is required.");
            }

            return value;
        }
        catch (JsonException)
        {
            throw new ValidationException("Request body is not valid JSON for this endpoint.");
        }
    }
}
