# NovaWallet Transfer API — Test Engineering Challenge

FirstBank Digital Factory take-home (QA Engineer track). A minimal reference Transfer API (Part A) plus the actual deliverable: a risk-based, automated test effort that would let a bank trust it before shipping (Part B).

## Repo layout

```
src/                    Part A — reference Transfer API (.NET 8)
tests/                  Part B — automated functional/boundary/negative/concurrency/idempotency/security suite
TEST_STRATEGY.md         Risk-based test strategy: what's tested, why, in what order, what's out of scope
AI_USAGE.md               How AI was used, concrete prompts, and where its suggestions were wrong for this domain
EXPLORATORY_TESTING_LOG.md  Time-boxed (≤60 min) exploratory session log
bug-reports/               One Markdown file per real bug found (see bug-reports/README.md for the format)
```

> `src/` and `tests/` are scaffolded next; this README's run instructions below will be finalized once they exist.

## The system under test (Part A)

A wallet/transfer API, in kobo (integer), with:

- `POST /wallets` — create a wallet, zero starting balance.
- `POST /wallets/{id}/credit` — deposit funds.
- `POST /transfers` — move funds between two wallets; idempotent via `Idempotency-Key`; atomic; never allows a negative balance.
- `GET /wallets/{id}` — balance, in kobo.
- A per-wallet daily outbound transfer limit (default ₦500,000/day) resetting at midnight **WAT (UTC+1)**, independent of server timezone.
- Bearer-token auth (mock/hardcoded token) on every endpoint.

This is a reference implementation for testing purposes, not production-grade — see `AI_USAGE.md` for how much of it was AI-scaffolded and what was corrected by hand.

## Running Part A locally

```bash
# TODO once scaffolded: dotnet run --project src/<ProjectName>
```

## Running the automated test suite (single command, CI-runnable)

```bash
# TODO once scaffolded: dotnet test
```

## Where to start reading

1. `TEST_STRATEGY.md` — the risk-based plan; read this first.
2. `AI_USAGE.md` — AI usage and judgment calls.
3. `bug-reports/` — issues found, each with severity, repro, and evidence.
4. `EXPLORATORY_TESTING_LOG.md` — the unscripted session and what it turned up.
