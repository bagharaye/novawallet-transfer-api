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

_To be filled in with the prompt used to generate the boundary/negative test cases, and the specific case (see below) where the generated tests needed correction for this domain._

## Where an AI suggestion was wrong or incomplete for this domain

_TODO — filled in with a real, specific instance once Part A/B exist. Candidates to watch for while building (both are called out in the brief as common failure modes, and both are actively being guarded against during implementation, not just written up after the fact):_

- **Float/decimal currency math.** If a generated test or implementation snippet compares balances with a tolerance (`assert abs(actual - expected) < 0.01`) or represents an amount as a decimal/float anywhere, that's wrong for this domain — Part A stores and compares kobo as integers, and a tolerance-based assertion would *hide* an off-by-one-kobo bug instead of catching it.
- **WAT vs. UTC daily-limit reset.** If a generated test asserts the daily limit resets using the *server's* local clock (e.g. `DateTime.Now` at midnight in whatever timezone CI runs in) rather than midnight WAT (UTC+1) specifically, it will pass in a CI runner that happens to be UTC and silently fail to catch a real bug — or worse, pass by coincidence and give false confidence.

This section will document the exact prompt, the exact wrong output, and the correction actually made — not a hypothetical.
