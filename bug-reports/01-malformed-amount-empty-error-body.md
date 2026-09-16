# [BUG-01] Non-integer `amountKobo` (e.g. a float) rejected with an empty `400` body instead of a JSON error

**Severity:** Medium
**Priority:** P2
**Component:** `POST /wallets/{id}/credit`, `POST /transfers` — request body binding for `amountKobo`
**Found by:** Manual smoke test while scaffolding Part A (before the automated suite existed)
**Status:** Fixed (commit in this repo — see `Services/TransferService.cs` / `Models/Dtos.cs`, `AmountParsing.TryGetKobo`)

## Severity/priority rationale

Not Critical/P0 because the *substance* of the currency-precision rule was never actually broken — a fractional kobo amount (`100.5`) was correctly rejected in both the buggy and fixed versions; money was never accepted with a fractional value. It's Medium/P2 because every API consumer (including the bank's own mobile/web clients and any integrator) depends on a consistent error contract to show the customer *why* a request failed; an empty `400 Bad Request` body gives a client nothing to act on or log, which is a real integration/debuggability problem for a payments API, just not a funds-safety one.

## Environment

- Commit: pre-fix state of `src/NovaWallet.Api` (first scaffold, before `AmountParsing` was introduced)
- How run: local `dotnet run`, in-memory store

## Repro steps (pre-fix)

```bash
curl -i http://127.0.0.1:5299/wallets/<walletId>/credit \
  -X POST \
  -H "Authorization: Bearer novawallet-dev-token" \
  -H "Content-Type: application/json" \
  -d '{"amountKobo": 100.5}'
```

(Same result for `POST /transfers` with a fractional `amountKobo`, and for a non-numeric value like `"amountKobo": "abc"`.)

## Expected result

`400 Bad Request` with the API's standard error body: `{"error": "validation_error", "message": "<why>"}`, exactly as every other validation failure in this API returns it (e.g. a missing `amountKobo` field already returned this shape correctly).

## Actual result

`400 Bad Request` with `Content-Length: 0` — no body at all.

```
HTTP/1.1 400 Bad Request
Content-Length: 0
Date: Wed, 16 Sep 2026 14:04:17 GMT
Server: Kestrel
```

## Root cause

`CreditRequest`/`TransferRequest` originally bound `amountKobo` straight to a CLR `long?`. When the JSON value wasn't a clean integer (a float like `100.5`, or a string), ASP.NET Core's minimal-API request-delegate factory threw internally while binding the body — *before* the endpoint handler or the app's custom `ExceptionHandlingMiddleware` ever ran — and the framework's default behavior for that internal binding failure is to set status `400` and return, with no response body and no `IProblemDetailsService` registered to fill one in. Because the failure never reached application code as a normal exception on the request pipeline, our own error-formatting logic never got a chance to run.

## Evidence

See "Actual result" above — reproduced via `curl -i`, `Content-Length: 0` and an empty body confirmed for both a fractional amount and a non-numeric amount.

## Fix

`amountKobo` is now bound as a raw `System.Text.Json.JsonElement` (`AmountParsing.TryGetKobo` in `Models/Dtos.cs`), which always binds successfully regardless of what JSON value was sent, and application code explicitly checks it's a JSON number and calls `JsonElement.TryGetInt64`. A rejection now always goes through the same `ValidationException` → `ExceptionHandlingMiddleware` path as every other bad request, producing a consistent `{"error": "validation_error", "message": "..."}` body. Re-verified after the fix:

```
HTTP/1.1 400 Bad Request
Content-Type: application/json

{"error":"validation_error","message":"amountKobo must be a whole number of kobo (fractional/decimal amounts are not allowed — money is stored as an integer number of kobo to avoid floating-point drift)."}
```
