# Test Strategy — NovaWallet Transfer API

**System under test:** the Part A reference Transfer API (`POST /wallets`, `POST /wallets/{id}/credit`, `POST /transfers`, `GET /wallets/{id}`), plus its cross-cutting rules: idempotent transfers, a per-wallet daily outbound limit resetting at midnight WAT, and bearer-token auth.

**Author's assumption for anything the brief leaves ambiguous is documented inline below**, per the brief's instruction to use judgment and record it.

---

## 1. Why risk-based, and how risk was scored

This is a payments API. The two failure modes that would actually hurt a bank are **money appearing or disappearing that shouldn't** (double-spend, negative balances, lost precision) and **money moving to/being seen by the wrong party** (authz bypass). Everything else — validation niceties, response shape, HTTP status conventions — matters for API quality but not for trust. Risk was scored on `impact (financial/regulatory/reputational) × likelihood (given this is a fresh, AI-scaffolded implementation)`, and test effort was allocated in that order, not in endpoint-listing order.

| # | Risk area | Impact | Likelihood | Priority |
|---|---|---|---|---|
| 1 | Concurrent transfers causing negative balance / double-spend | Severe (funds created from nothing) | High (naive scaffolds rarely lock correctly) | **P0** |
| 2 | Idempotency-Key not honored (replay creates a duplicate transfer) | Severe (customer debited twice) | High (easy to get subtly wrong) | **P0** |
| 3 | Currency precision (kobo truncation/rounding/float drift) | Severe (silent value drift, hard to detect in prod) | Medium–High (AI scaffolds default to float/decimal without being told) | **P0** |
| 4 | Daily outbound limit bypassed, or reset uses server-local time instead of WAT | High (regulatory/consumer-protection exposure — CBN transaction-limit rules) | High (WAT-vs-UTC is a deliberately easy edge to miss) | **P1** |
| 5 | AuthN/AuthZ bypass (missing token, wrong wallet's data reachable) | Severe (NDPA data-exposure + unauthorized funds movement) | Medium | **P1** |
| 6 | Standard functional/boundary/negative correctness per endpoint | Medium | Medium | **P2** |
| 7 | Injection on free-text inputs | Low (no SQL/shell surface expected in a wallet/transfer API, but cheap to check) | Low | **P2** |
| 8 | Performance/throughput under load | Medium (UX, not correctness) | Unknown | **P3 / stretch** |

## 2. What is being tested, and in what order

Work proceeds in the order below. Each stage is a hard gate loosely — later stages assume earlier ones pass, since a system that can't hold balances correctly isn't worth load-testing.

1. **Functional + boundary + negative, per endpoint** (`docs`/automated suite, tagged `@functional`)
   Happy path, missing/malformed fields, wrong types, zero/negative amounts, non-existent wallet IDs, and the kobo-integer boundary (0, 1 kobo, largest safe integer, fractional/float amounts rejected).
2. **Currency precision** (tagged `@money`)
   Explicit assertions that stored/returned balances are integers, that repeated small credits sum exactly, and that no code path accepts or silently coerces a float/decimal amount. This is called out separately from "boundary values" because it's the hard constraint the brief flags, and float-precision bugs don't show up as an obviously "wrong" number — they show up as a number that's wrong by 1 kobo, which is easy to wave away as "close enough" if you're not looking for it.
3. **Idempotency** (tagged `@idempotency`)
   Same key + same payload replayed → identical response, balance charged once. Same key + different payload → rejected (409/422, exact status documented against actual behavior), original transfer untouched. Missing key → treated per documented behavior (assumption recorded in §4).
4. **Concurrency** (tagged `@concurrency`)
   Many simultaneous transfers against one wallet with just enough balance for one to succeed — proof (response bodies + final `GET /wallets/{id}` balance), not assertion, of whether the balance ever went negative or more than one transfer succeeded.
5. **Daily limit + WAT reset** (tagged `@limit`)
   Limit enforced at the boundary (exact limit passes, limit+1 kobo rejected), and — the specific edge the brief calls out — the reset instant is midnight **WAT (UTC+1)**, verified independently of the server's own local timezone (i.e., the test doesn't just trust `new Date()` on the server; it computes the WAT boundary itself and checks behavior either side of it, using a fakeable/injectable clock rather than sleeping until real midnight).
6. **Security pass** (tagged `@security`)
   No token / malformed token / wrong token → 401. Valid token for wallet A used to read or credit wallet B → checked against actual behavior (Part A's spec only requires *a* bearer check, not per-wallet ownership, so this stage's job is to find out which behavior was actually built and flag it if it's the permissive one). Basic injection payloads (`' OR 1=1--`, `<script>`, oversized strings, null bytes) in wallet IDs and free-text fields.
7. **Exploratory session** (≤ 60 min, time-boxed, logged separately in `EXPLORATORY_TESTING_LOG.md`)
   Unscripted, after the scripted suite is green, specifically hunting for what the checklist above wouldn't think to ask — sequencing bugs, error-message leakage, weird-but-valid inputs.
8. **Stretch, only if time remains:** basic load test (k6/Locust), CI wiring, fixture/factory cleanup.

## 3. Explicitly out of scope for this exercise

- **The other four NovaPay modules** (NovaSave, NovaLend, NovaBiz, diaspora remittance) — Part A only implements the Transfer API; nothing to test there.
- **Real NIBSS NIP rail integration** — Part A settlement is in-process/simulated; no real interbank rail exists to test against.
- **Real BVN/NIN KYC verification** — no identity-verification endpoint exists in Part A's scope.
- **USSD channel (`*894#`) behavior** — no USSD gateway is implemented; only the HTTP API is tested.
- **Full penetration test** — the security pass is a competent first look (authn/authz bypass, obvious injection), not fuzzing, not a professional pen-test, not infra-level testing (TLS config, header hardening, dependency CVE scanning).
- **Multi-currency / FX** — NovaPay is NGN-only per the brief; no FX conversion exists to test.
- **Formal regulatory compliance audit** (CBN licensing conditions, full NDPA data-handling audit) — noted where it's directly relevant (the WAT limit reset, PII exposure in error responses) but not exhaustively verified; this exercise doesn't have a compliance function to check against.
- **Production-scale performance/chaos testing** — only a basic stretch-goal load smoke test, not capacity planning, not failover/DR testing, not DB-failure injection.
- **UI testing** — there is no UI in Part A.
- **Token lifecycle** (issuance, expiry, refresh) — Part A uses a single mock/hardcoded bearer token; testing covers presence/correctness of the check, not a real auth-server flow.

## 4. Assumptions (where the brief is ambiguous)

- Wallet IDs are treated as opaque strings/identifiers; tests don't assume a particular format (UUID vs incrementing int) beyond what Part A actually returns.
- "Must not allow balance to go negative" is read as applying to the *source* wallet only; the destination wallet's balance has no upper bound asserted beyond kobo-integer overflow sanity.
- A transfer request without an `Idempotency-Key` header is assumed to be processed as a one-off, non-replayable request (each such call can create a new transfer) unless Part A documents otherwise — this assumption is checked and corrected against actual behavior once Part A exists.
- The daily limit is assumed to apply to the **sum of outbound transfer amounts** initiated by a wallet within the WAT calendar day, not to individual transfer size, and not to inbound credits.
- A transfer that **fails** (wallet not found, insufficient funds, daily limit exceeded) is **not** cached under its `Idempotency-Key` — a failed request has no side effect to protect against duplicating, so a client is allowed to retry the identical key+payload after resolving the underlying condition (e.g. topping up the wallet) and have it succeed. Only a request that actually moved money is cached and permanently replayed. Confirmed by design and locked in by `IdempotencyTests.FailedTransfer_IsNotCachedUnderTheKey_RetryCanSucceedOnceConditionIsResolved`.
- "Resetting at midnight WAT regardless of server timezone" is tested by driving the system clock (or an injectable clock dependency) rather than waiting for real midnight, since real-time waiting isn't practical in a CI-run suite.

## 5. What we did not have time to test, and how it would be prioritized with one more week

Given the ~48–72 hour window, the following were consciously deferred, in the order they'd be picked back up:

1. **Concurrency beyond the single-wallet double-spend case** — concurrent idempotency-key replay races (same key, truly simultaneous, not sequential-fast), concurrent credit + transfer interleavings, and concurrent transfers that *individually* fit under the daily limit but collectively exceed it (a TOCTOU race on the limit check, analogous to the balance race).
2. **Fault-injection / resilience** — behavior when the datastore is unavailable mid-transfer, partial-write recovery, and whether a crash between "debit" and "credit" can leave the system in an inconsistent state.
3. **Deeper security** — timing-based token comparison, rate limiting/brute-force on the token, header-based tricks (e.g., conflicting `Content-Length`, unexpected `Content-Type`), and a wider injection/fuzz corpus (property-based testing on amount and ID fields).
4. **Load/performance at realistic scale** — the stretch-goal k6 run here is a smoke test (confirms the API survives concurrent load without falling over), not a capacity or latency SLO study; that would need production-like data volumes and a proper baseline.
5. **Long-running/scheduled behavior** — the WAT-reset test above proves correctness *at* the boundary; it doesn't run for real multi-day soak time to catch drift (leap seconds, DST-adjacent host misconfiguration, clock skew between app and DB).
6. **Contract/consumer testing** — no schema (OpenAPI) validation layer was added to lock the response shape against accidental breaking changes.

With a week, priority order would follow the risk table in §1: the concurrency/idempotency race conditions first (highest financial blast radius and the least covered by the time-boxed suite), then fault injection, then the deeper security pass, then load testing.
