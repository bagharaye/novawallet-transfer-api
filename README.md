# NovaWallet Transfer API — Test Engineering Challenge

FirstBank Digital Factory take-home (QA Engineer track). A minimal reference Transfer API (Part A) plus the actual deliverable: a risk-based, automated test effort that would let a bank trust it before shipping (Part B).

## Repo layout

```
src/NovaWallet.Api/         Part A — reference Transfer API (.NET 8 minimal API)
tests/NovaWallet.Tests/     Part B — automated functional/boundary/negative/concurrency/idempotency/security suite (xUnit)
load/                       Stretch goal — basic k6 load smoke test + findings note
.github/workflows/ci.yml    Stretch goal — GitHub Actions: builds and runs the suite on every push
TEST_STRATEGY.md            Risk-based test strategy: what's tested, why, in what order, what's out of scope
AI_USAGE.md                 How AI was used, concrete prompts, and where its suggestions were wrong for this domain
EXPLORATORY_TESTING_LOG.md  Time-boxed (≤60 min) exploratory session log
bug-reports/                One Markdown file per real bug found (see bug-reports/README.md for the format)
```

## The system under test (Part A)

A wallet/transfer API, in kobo (integer), with:

- `POST /wallets` — create a wallet, zero starting balance.
- `POST /wallets/{id}/credit` — deposit funds.
- `POST /transfers` — move funds between two wallets; idempotent via `Idempotency-Key`; atomic; never allows a negative balance.
- `GET /wallets/{id}` — balance, in kobo.
- A per-wallet daily outbound transfer limit (default ₦500,000/day = 50,000,000 kobo) resetting at midnight **WAT (UTC+1)**, independent of server timezone.
- Bearer-token auth (mock/hardcoded token) on every endpoint.

This is a reference implementation for testing purposes, not production-grade — an in-memory store, a single shared bearer token, no persistence. See `AI_USAGE.md` for how much of it was AI-scaffolded and what was corrected by hand, and `bug-reports/` for what testing it turned up (including in this implementation itself).

## Prerequisites

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Check with `dotnet --version` (should print `8.x`).

## Running Part A locally

```bash
dotnet run --project src/NovaWallet.Api
```

Starts on `http://localhost:5229` (see `src/NovaWallet.Api/Properties/launchSettings.json`; override with `ASPNETCORE_URLS` if you need a different port). Every endpoint requires `Authorization: Bearer novawallet-dev-token` (the mock token, configurable via `Api:BearerToken` in `src/NovaWallet.Api/appsettings.json`). Example:

```bash
curl -X POST http://localhost:5229/wallets -H "Authorization: Bearer novawallet-dev-token"
```

## Running the automated test suite (single command, CI-runnable)

```bash
dotnet test
```

Runs from the repo root — the solution file (`NovaWallet.sln`) references both `src/NovaWallet.Api` and `tests/NovaWallet.Tests`, so this one command restores, builds, and runs all 62 tests. No external services, no database, no manually-started server required — the test project boots the real app in-process via `WebApplicationFactory<Program>`. This is exactly what `.github/workflows/ci.yml` runs on every push.

## Running the load smoke test (stretch goal)

See `load/README.md` — requires [k6](https://k6.io) and a running instance of Part A.

## Where to start reading

1. `TEST_STRATEGY.md` — the risk-based plan; read this first.
2. `AI_USAGE.md` — AI usage and judgment calls.
3. `bug-reports/` — issues found, each with severity, repro, and evidence.
4. `EXPLORATORY_TESTING_LOG.md` — the unscripted session and what it turned up.
