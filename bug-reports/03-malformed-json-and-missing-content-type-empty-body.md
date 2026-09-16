# [BUG-03] Syntactically invalid JSON and requests without `Content-Type: application/json` bypass the error contract

**Severity:** Medium
**Priority:** P2
**Component:** `POST /wallets/{id}/credit`, `POST /transfers` — request body parsing
**Found by:** Time-boxed exploratory testing session (see `EXPLORATORY_TESTING_LOG.md`), after the scripted suite and BUG-01/BUG-02 were already fixed
**Status:** Fixed (`Models/RequestBodyReader.cs`; `Program.cs` now reads/deserializes the body explicitly instead of relying on minimal-API implicit binding)

## Severity/priority rationale

Same class of problem as BUG-01 (Medium/P2, not Critical) — no money-safety property was ever actually broken; these requests were always rejected, never silently accepted. It matters because it's a *second, broader* instance of the same root cause BUG-01's fix only partially closed: BUG-01 fixed `amountKobo` specifically being a wrong *type* inside otherwise-valid JSON. This is genuinely invalid JSON *syntax*, and separately, a POST with no (or the wrong) `Content-Type` header — both are realistic things a real client (a slightly-off mobile app build, a proxy that drops headers, a hand-rolled integration) will send sooner or later, and both left the caller with nothing to act on.

## Environment

- Commit: state after BUG-01/BUG-02 fixes, before `Models/RequestBodyReader.cs` existed
- How run: local `dotnet exec`, manual `curl` probing during the exploratory session

## Repro steps

```bash
# Malformed JSON syntax (unbalanced braces, unquoted key)
curl -i http://localhost:5299/wallets/<id>/credit \
  -H "Authorization: Bearer novawallet-dev-token" -H "Content-Type: application/json" \
  -d '{amountKobo: 100'

# No Content-Type header at all, with an otherwise-valid JSON body
curl -i http://localhost:5299/wallets/<id>/credit \
  -H "Authorization: Bearer novawallet-dev-token" \
  -d '{"amountKobo": 500}'
```

## Expected result

Malformed JSON: `400 Bad Request` with `{"error": "validation_error", "message": "..."}`.
Missing `Content-Type`: either accepted (the body is valid JSON regardless of the header) or rejected with the same standard error body — either is defensible, but not a silent, unexplained rejection.

## Actual result

```
# Malformed JSON:
HTTP/1.1 400 Bad Request
Content-Length: 0

# Missing Content-Type:
HTTP/1.1 415 Unsupported Media Type
Content-Length: 0
```

Both empty bodies, same symptom as BUG-01.

## Root cause

Same underlying mechanism as BUG-01: minimal API's implicit body binding for a complex parameter type (`CreditRequest`, `TransferRequest`) runs inside ASP.NET Core's generated request-delegate code, which handles its own binding failures — invalid JSON syntax, or a Content-Type that doesn't say "this is JSON" — by writing a bare status code and returning, before the endpoint handler body (or `ExceptionHandlingMiddleware`) ever executes. BUG-01's fix (parsing `amountKobo` from a `JsonElement`) only addressed *type* mismatches within an otherwise well-formed, correctly-content-typed JSON body; it didn't change how the *body itself* gets bound, so syntactically invalid JSON and content-type mismatches still went through the same framework short-circuit. (`builder.Services.AddProblemDetails()` was tried first, on the theory it would make the framework fill in that empty body automatically — verified with the same repro steps above — but it had no effect on either case, so it isn't in the final fix.)

## Fix

`Program.cs` no longer lets minimal API implicitly bind `CreditRequest`/`TransferRequest` from the body at all. Both `POST /wallets/{id}/credit` and `POST /transfers` now take `HttpRequest` directly and call `RequestBodyReader.ReadAsync<T>(httpRequest)`, which deserializes the raw request stream itself and converts any `JsonException` (or a null/missing body) into the standard `ValidationException`. This runs as ordinary code inside the endpoint handler, downstream of `ExceptionHandlingMiddleware`, so every rejection — a bad type, invalid syntax, or an empty body — now gets the same `{"error", "message"}` shape. It also no longer requires a specific `Content-Type` header to accept a JSON body, which was a deliberate simplification for this reference implementation rather than an oversight: the header wasn't being validated against anything else, so gating parsing on it only added another way to reject a perfectly valid request.

Re-verified after the fix (see `FunctionalTests.Credit_SyntacticallyInvalidJson_Returns400WithErrorBody` and `Credit_EmptyBody_Returns400WithErrorBody`):

```
HTTP/1.1 400 Bad Request
Content-Type: application/json

{"error":"validation_error","message":"Request body is not valid JSON for this endpoint."}
```
