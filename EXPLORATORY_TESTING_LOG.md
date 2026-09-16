# Exploratory Testing Session Log

**Time-box:** ≤ 60 minutes (approx. 45 minutes used)
**Status:** Complete. Run after the scripted automated suite (functional, currency, idempotency, concurrency, limit/WAT, security — 59 tests at the time) and after `bug-reports/01` and `bug-reports/02` were already found and fixed via targeted probing. This session's job was to spend its budget on what neither the checklist nor the earlier targeted probes would think to ask.

## Charter

Explore the NovaWallet Transfer API's endpoints (`POST /wallets`, `POST /wallets/{id}/credit`, `POST /transfers`, `GET /wallets/{id}`) looking for behavior a scripted checklist wouldn't catch: unusual HTTP-level input (wrong methods, malformed bodies, missing/odd headers), surprising interactions between features, error-message content and consistency, and anything that "feels wrong" even without a pre-planned assertion for it.

## Session log

| Time | Action taken | Observation | Bug filed? |
|---|---|---|---|
| 0:00 | Wrong HTTP method on existing routes (`GET /transfers`, `DELETE /wallets/x`, `PUT /wallets`) | All correctly `405 Method Not Allowed` — ASP.NET Core's routing handles this automatically. Working as expected. | No |
| 0:05 | Sent syntactically invalid JSON (`{amountKobo: 100` — unbalanced, unquoted key) to `POST /wallets/{id}/credit` | `400 Bad Request` with **empty body** (`Content-Length: 0`). Same symptom as bug-01, but that fix only covered wrong-*typed* values inside otherwise-valid JSON — this is invalid JSON *syntax*, a different code path. | **Yes — bug-03** |
| 0:10 | Sent a valid JSON body with no `Content-Type` header at all | `415 Unsupported Media Type`, also empty body. Same underlying cause as above (minimal API's implicit body binding short-circuits before the app's own error-handling middleware runs). | Folded into bug-03 |
| 0:16 | Tried `AddProblemDetails()` as a possible framework-level fix for the above two | No effect on either case — verified with the same repro steps, still empty bodies. Documented as a dead end in bug-03 rather than silently abandoned. | — |
| 0:22 | Rewrote body parsing to read the request stream explicitly (`RequestBodyReader`) instead of relying on implicit binding; re-tested malformed JSON, empty body, and missing Content-Type | All three now return the standard `{"error", "message"}` body. Missing `Content-Type` is now tolerated (JSON is parsed regardless of the header) rather than rejected — a deliberate simplification, noted in bug-03. Full regression suite still green (61/61 at this point). | Fixed |
| 0:30 | Idempotency-Key header sent with different casing (`idempotency-key`, `IDEMPOTENCY-KEY`) across two otherwise-identical transfer calls | Second call correctly replayed the first (balance only debited once) — HTTP headers are case-insensitive per spec and ASP.NET Core's header dictionary honors that. Working as expected, not a bug. | No |
| 0:34 | `amountKobo` sent as JSON `true`, as an array `[1000]`, and as explicit `null` | All three cleanly rejected with the standard validation error, no crash, no confusing message. `AmountParsing.TryGetKobo`'s `ValueKind == Number` check handles all of them uniformly. | No |
| 0:37 | `amountKobo` sent as `-9223372036854775808` (`long.MinValue`) | Cleanly rejected as "must be a positive integer number of kobo" — no crash from negating/handling the edge-of-range value. | No |
| 0:40 | `fromWalletId` sent as a whitespace-only string (`"   "`) | Correctly rejected as required/missing (the `IsNullOrWhiteSpace` check catches it, not just `IsNullOrEmpty`). | No |
| 0:42 | Replayed a **failed** transfer (insufficient funds → later, wallet-not-found) under the same `Idempotency-Key`, twice | Both attempts independently re-evaluated and failed the same way — the failure is not cached under the key. Investigated the implementation to confirm this is deliberate: a failed transfer has no side effect, so there's nothing to protect against duplicating, and a client can retry the same key after fixing the condition (e.g. topping up the wallet). Turned into a permanent regression test rather than a bug — see `TEST_STRATEGY.md` §4 (assumptions) and `IdempotencyTests.FailedTransfer_IsNotCachedUnderTheKey_RetryCanSucceedOnceConditionIsResolved`. | No (documented as intended behavior) |
| 0:45 | Extra/unknown fields in a request body (`{"amountKobo": 500, "note": "hello", "currency": "USD"}`) | Silently ignored, request processed normally. Reasonable, tolerant default for a JSON API. Not a bug. | No |

## Summary

- Duration actually used: ~45 minutes
- Number of new issues found: 1 (bug-03), found, fixed, and covered by permanent regression tests within the session
- One additional item (idempotency-key replay of a failed request) investigated and confirmed as intended behavior rather than a bug — documented as an assumption and locked in with a regression test
- Bug reports filed: see `bug-reports/` (bug-01 and bug-02 were found via targeted probing before this session; bug-03 was found during it)
