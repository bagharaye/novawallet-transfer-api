# AI Usage

This document is filled in progressively as the task proceeds — it is honest about what the AI got right, what it got wrong, and where a human call was needed. Entries are added chronologically as work happens, not written retroactively from memory.

## Tools used

| Tool | What it was used for |
|---|---|
| Claude (Claude Code, `claude-sonnet-5`) | Scaffolding Part A (the .NET Transfer API), drafting Part B's automated test suite, drafting `TEST_STRATEGY.md`, and this file. Used interactively, turn by turn, reviewing and editing every generated file rather than accepting output wholesale. |

_(Section will be extended if additional tools — e.g. GitHub Copilot in-editor, a separate model for a second opinion — are used later in the task.)_

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

## Where an AI suggestion was wrong or incomplete for this domain

**The oversized-input test above** is the concrete instance (full detail in §3): a generated security test looked reasonable, executed, and failed — but it was testing `HttpClient`'s URI-length limit, not the API. It's a version of the same underlying trap the brief calls out for float currency math and the WAT/UTC reset: a test that superficially exercises "the right idea" (oversized/malformed input handling) without accounting for where in the stack a *specific* domain's inputs are actually carried (path segment vs. body field, for a REST API with ID-in-path routes).

The two traps the brief names directly were designed around rather than caught after the fact, and it's worth being explicit about why they didn't slip through:

- **Float/decimal currency math** — avoided at the design stage by binding `amountKobo` as a raw `JsonElement` and requiring `TryGetInt64` to succeed (`Models/Dtos.cs`), and by writing `CurrencyPrecisionTests` to assert a fractional amount is *rejected*, not rounded/truncated, checking the wallet balance stayed untouched rather than just checking the HTTP status code.
- **WAT vs. UTC daily-limit reset** — avoided by introducing the `IClock`/`TestClock` seam specifically so `DailyLimitTests.DailyLimit_ResetsAtWatMidnight_AnHourBeforeTheUtcCalendarDateRolls` could assert the reset at `23:00 UTC` (WAT midnight) while the UTC calendar date is still the previous day — the exact moment a UTC-midnight-based implementation would still (wrongly) refuse the transfer.
