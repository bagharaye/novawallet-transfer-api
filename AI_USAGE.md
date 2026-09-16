# AI Usage

This document is filled in progressively as the task proceeds — it is honest about what the AI got right, what it got wrong, and where a human call was needed. Entries are added chronologically as work happens, not written retroactively from memory.

## Tools used

| Tool | What it was used for |
|---|---|
| Claude (Claude Code, `claude-sonnet-5`) | Scaffolding Part A (the .NET Transfer API), drafting Part B's automated test suite, drafting `TEST_STRATEGY.md`, running the exploratory session, and writing this file, `bug-reports/`, and the load test. Used interactively, turn by turn, reviewing and editing every generated file and every claim ("the fix worked") against actual `dotnet test`/`curl` output rather than accepting output wholesale. |
| k6 | Not AI — listed for completeness. Used for the stretch-goal load smoke test (`load/`); the test script itself was hand-written against k6's own API, not AI-generated. |

## Concrete prompts and what came back

> Prompts below are lightly trimmed for length; intent and wording are preserved.

### 1. Drafting the risk-based test strategy

**Prompt:** *"Come up with all the MD files needed first before we implement anything"* (applied against the full FirstBank NovaWallet Transfer API take-home brief).

**What came back:** A `TEST_STRATEGY.md` with a risk table scoring impact × likelihood per area, an explicit test order, an out-of-scope list, and a "what we didn't have time for" section. Reviewed and kept largely as generated — the risk-ordering logic (concurrency/idempotency/precision before standard CRUD boundary testing) matched the reviewer's own judgment about where a payments API actually breaks.

_(Further entries added below as Part A and Part B are built.)_

### 2. Scaffolding Part A's wallet/transfer/idempotency/limit logic

**Prompt:** *"Go"* (after the earlier planning turn had already agreed the plan: .NET 8 minimal API, in-memory store, per-wallet async locks for atomicity, an `IClock` abstraction so the WAT-reset rule is testable without sleeping until real midnight, and `Idempotency-Key` handled via a per-key lock + cached response).

**What came back:** A working minimal API — `Program.cs`, `TransferService`, `WalletStore`, middleware for bearer auth and centralized error formatting — bound `amountKobo` directly to a C# `long?` on the request DTOs, on the reasonable-looking assumption that ASP.NET Core's model binder rejecting a non-integer JSON value (like `100.5`) was good enough. Manually smoke-testing it immediately after (`curl` with `"amountKobo": 100.5`) showed the rejection *worked* — no fractional amount was ever accepted — but the response was `400` with a completely empty body, because that particular kind of binding failure is handled inside ASP.NET Core's own request-delegate machinery, before the app's `ExceptionHandlingMiddleware` (or even the endpoint handler) ever runs. So a client gets no explanation, no `{"error", "message"}` — just a blank rejection. That's exactly the kind of thing that "looks right" in a happy-path check and is wrong for a payments API, where every rejection needs to be actionable by the calling client. See `bug-reports/01-malformed-amount-empty-error-body.md` for the full writeup.

**Correction made:** Rewrote `amountKobo` binding to take a raw `System.Text.Json.JsonElement` and validate it explicitly (`AmountParsing.TryGetKobo`), so *every* rejection — missing field, wrong type, or fractional value — goes through the same `ValidationException` path and gets the same JSON error shape. This also made the precision check explicit and readable in one place instead of being an implicit side effect of a CLR type choice.

### 3. Generating the automated test suite

**Prompt:** *"Go"* (continuing the same session, after Part A was scaffolded and the risk-ordered test plan in `TEST_STRATEGY.md` §2 was already agreed: functional → currency precision → idempotency → concurrency → daily limit/WAT → security, one xUnit class per stage, all driven through the real HTTP pipeline via `WebApplicationFactory<Program>`).

**What came back:** Seven test classes (57 test cases total), including a `SecurityTests.Credit_OversizedWalletId_DoesNotCauseServerError` case meant to prove the API doesn't 500 on an oversized wallet ID — a reasonable thing to want to check, since `{id}` is user-controlled input on a public endpoint. It built the oversized (100,000-character) ID straight into the URL path via `HttpClient.PostAsync($"/wallets/{oversizedId}/credit", ...)`. Running the suite immediately failed that one test — not with the server behavior it was trying to check, but with a client-side `System.UriFormatException: Invalid URI: The Uri string is too long`, thrown by `HttpClient` itself before any request left the process. The test never touched the server at all; it was actually asserting a fact about .NET's own `Uri` class.

**Where this was wrong for this domain:** it's a REST API where several endpoints take an identifier as a URL path segment (`/wallets/{id}`, `/wallets/{id}/credit`) rather than in the request body. A generic "send an oversized string and check for a graceful failure" test pattern doesn't account for that — it needs to know *which* of this specific API's inputs are path-bound (where the HTTP client's own URI limits get hit first) versus body-bound (where the value actually reaches the server's model binding and business logic). Applied uncritically, the test gave a false sense of coverage: it "ran" and even "failed clearly," but for the wrong reason, and would never have caught an actual server-side crash on a large ID.

**Correction made:** rewrote it as `Transfer_OversizedWalletId_DoesNotCauseServerError`, moving the oversized value into a JSON body field (`POST /transfers` with an oversized `fromWalletId`) — a field the server's own binding and lookup logic actually has to process — and asserting on the server's response instead of hoping the client library forwards the request unmodified.

### 4. Probing the AI-scaffolded balance arithmetic for currency-specific failure modes

**Prompt:** *"Let's do a deliberate bug hunt for a class of issue the automated suite hasn't covered yet"* (self-directed, after the scripted suite was green — asking specifically what a careful reviewer would still be suspicious of in the original AI-generated `TransferService`, given the brief's emphasis on currency precision).

**What came back:** the balance fields were plain C# `long` with ordinary `+=`/`-=` arithmetic (`wallet.BalanceKobo += amountKobo;`), generated without any overflow handling — a very ordinary, "looks correct" way to add two integers. A quick probe test (credit a wallet to within 500 kobo of `long.MaxValue`, then credit it again) confirmed the suspicion immediately: C#'s default *unchecked* arithmetic silently wraps a `long` addition that overflows into a large **negative** number, and the endpoint still returned `200 OK`. That's the single outcome this entire domain's hard constraint says must never happen — a negative wallet balance — produced by the most basic possible operation in the whole system, with no exception, no error, nothing to flag it. See `bug-reports/02-int64-kobo-overflow-negative-balance.md`.

**Where this was wrong for this domain:** generated code that is textbook-correct C# (unchecked arithmetic is the language default; nothing here is a "bug" by general-purpose standards) is *wrong* the moment the two operands represent money and the invariant "balance never goes negative" is a hard, stated requirement rather than a nice-to-have. Generic code generation has no reason to reach for `checked` arithmetic unless prompted specifically about numeric-overflow safety for a monetary domain — it's exactly the kind of domain-specific edge that a general "write me a wallet service" prompt won't surface on its own.

**Correction made:** added an explicit pre-mutation bounds check before crediting a wallet or moving funds into a destination wallet (rather than a `checked`/`catch` wrapping both the debit and credit statements together, which — on review — would have risked applying the debit and leaving the credit unapplied if only the second overflowed, silently losing money from the source wallet). Both paths now reject with a clear `400 validation_error` instead of wrapping.

### 5. Exploratory session — generalizing the first "empty error body" fix

**Prompt:** *"Continue the exploratory testing session"* (self-directed probing after the scripted suite and both known bugs were fixed, per the charter in `EXPLORATORY_TESTING_LOG.md`: try HTTP-level inputs a scripted checklist wouldn't think to script).

**What came back / where the earlier fix was incomplete:** the AI-suggested fix for bug-01 (parsing `amountKobo` from a raw `JsonElement`) only addressed a wrong-*typed* value inside otherwise well-formed JSON. Probing with genuinely invalid JSON *syntax* (`{amountKobo: 100`, unbalanced) and a request with no `Content-Type` header reproduced the exact same empty-body symptom via a different code path — the fix had patched one specific field's binding, not the underlying cause (minimal API's implicit body binding short-circuiting before the app's own error-handling code runs). A first attempted fix (`builder.Services.AddProblemDetails()`, on the reasonable-sounding theory that it would make the framework fill in the missing body automatically) was tested against the same repro steps and had **no effect on either case** — worth noting as a case where an AI-suggested fix didn't work, and the honest thing was to say so and try something else rather than assume it worked because it compiled.

**Correction made:** stopped relying on minimal API's implicit binding entirely for these two endpoints; both now read and deserialize the request body explicitly (`RequestBodyReader`), which runs as ordinary handler code downstream of the app's own middleware and can't be short-circuited by the framework. See `bug-reports/03-malformed-json-and-missing-content-type-empty-body.md`.

## Where an AI suggestion was wrong or incomplete for this domain

Four concrete instances surfaced across this session, in increasing order of how domain-specific they are:

1. **§3 — the oversized-input test.** A generated security test looked reasonable, executed, and failed — but it was testing `HttpClient`'s own URI-length limit, not the API, because it put the payload in a URL path segment for an API where some IDs are path-bound. A version of the same underlying trap the brief calls out for float currency math and the WAT/UTC reset: a test that superficially exercises "the right idea" without accounting for where in *this specific* stack the input actually travels.
2. **§4 — the int64 kobo overflow.** The single most domain-specific finding: ordinary, textbook-correct C# arithmetic (unchecked `+=` on a `long`) is a real bug the moment the value is money with a stated "never negative" invariant. No generic prompt would surface this without someone asking, specifically, "what happens at the numeric edges of this monetary type."
3. **§5 — the incomplete first fix, and a fix that didn't work.** The initial correction for bug-01 only closed one specific code path (a wrong-typed field), not the general cause (implicit body binding short-circuiting the app's error handling) — found by continuing to probe after the first fix looked done. Also worth being honest about: the first *attempted* fix for the generalized issue (`AddProblemDetails()`) didn't actually change the observed behavior at all when re-tested — it looked like a plausible, idiomatic ASP.NET Core answer, and wasn't, and the fastest way to find that out was re-running the exact repro steps rather than trusting that it should work.
4. **The two traps the brief names directly** — float currency math and the WAT/UTC reset — were designed around at the outset rather than caught after the fact, which is worth being explicit about rather than claiming credit for "catching" something that was never actually wrong:

The two traps the brief names directly were designed around rather than caught after the fact, and it's worth being explicit about why they didn't slip through:

- **Float/decimal currency math** — avoided at the design stage by binding `amountKobo` as a raw `JsonElement` and requiring `TryGetInt64` to succeed (`Models/Dtos.cs`), and by writing `CurrencyPrecisionTests` to assert a fractional amount is *rejected*, not rounded/truncated, checking the wallet balance stayed untouched rather than just checking the HTTP status code.
- **WAT vs. UTC daily-limit reset** — avoided by introducing the `IClock`/`TestClock` seam specifically so `DailyLimitTests.DailyLimit_ResetsAtWatMidnight_AnHourBeforeTheUtcCalendarDateRolls` could assert the reset at `23:00 UTC` (WAT midnight) while the UTC calendar date is still the previous day — the exact moment a UTC-midnight-based implementation would still (wrongly) refuse the transfer.
