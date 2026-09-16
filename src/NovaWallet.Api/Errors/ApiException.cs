using System.Net;

namespace NovaWallet.Api.Errors;

/// <summary>
/// A domain/validation failure that maps directly to an HTTP status code and
/// a machine-readable error code, so endpoint handlers can just throw and let
/// the exception-handling middleware turn it into a consistent JSON body.
/// </summary>
public class ApiException(HttpStatusCode statusCode, string errorCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
}

public sealed class WalletNotFoundException(string walletId)
    : ApiException(HttpStatusCode.NotFound, "wallet_not_found", $"Wallet '{walletId}' was not found.");

public sealed class ValidationException(string message)
    : ApiException(HttpStatusCode.BadRequest, "validation_error", message);

public sealed class InsufficientFundsException(string walletId)
    : ApiException(HttpStatusCode.UnprocessableEntity, "insufficient_funds",
        $"Wallet '{walletId}' does not have sufficient balance for this transfer.");

public sealed class DailyLimitExceededException(string walletId, long limitKobo)
    : ApiException(HttpStatusCode.UnprocessableEntity, "daily_limit_exceeded",
        $"Wallet '{walletId}' would exceed its daily outbound limit of {limitKobo} kobo.");

public sealed class IdempotencyKeyConflictException(string idempotencyKey)
    : ApiException(HttpStatusCode.Conflict, "idempotency_key_conflict",
        $"Idempotency-Key '{idempotencyKey}' was already used with a different request payload.");

public sealed class UnauthorizedApiException()
    : ApiException(HttpStatusCode.Unauthorized, "unauthorized", "Missing or invalid bearer token.");
