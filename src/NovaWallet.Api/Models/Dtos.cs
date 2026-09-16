using System.Text.Json;

namespace NovaWallet.Api.Models;

public sealed record WalletResponse(string Id, long BalanceKobo, DateTimeOffset CreatedAt);

public sealed record CreditRequest(JsonElement? AmountKobo);

public sealed record TransferRequest(string? FromWalletId, string? ToWalletId, JsonElement? AmountKobo);

public sealed record TransferResponse(
    string Id,
    string FromWalletId,
    string ToWalletId,
    long AmountKobo,
    DateTimeOffset CreatedAt);

public sealed record ErrorResponse(string Error, string Message);

/// <summary>
/// Parses "amountKobo" from a raw <see cref="JsonElement"/> rather than binding it
/// straight to <c>long</c>. Binding straight to <c>long</c> made ASP.NET Core's own
/// model binder reject a fractional amount (e.g. <c>100.5</c>) before our code ever
/// ran, short-circuiting with an empty response body instead of our standard
/// { "error", "message" } shape. Parsing it ourselves keeps that rejection — a
/// fractional/non-integer kobo amount must still be rejected, per the hard
/// currency-precision constraint — but routes it through the same validation
/// error path as every other bad request, with a message that says why.
/// </summary>
public static class AmountParsing
{
    public static bool TryGetKobo(JsonElement? element, out long amountKobo, out string? error)
    {
        amountKobo = 0;
        error = null;

        if (element is not { ValueKind: JsonValueKind.Number } value)
        {
            error = "amountKobo is required and must be a whole number (integer kobo, no decimals).";
            return false;
        }

        if (!value.TryGetInt64(out amountKobo))
        {
            error = "amountKobo must be a whole number of kobo (fractional/decimal amounts are not allowed — " +
                    "money is stored as an integer number of kobo to avoid floating-point drift).";
            return false;
        }

        return true;
    }
}
