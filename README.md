# NovaWallet Transfer API — Test Engineering Challenge

FirstBank Digital Factory take-home (QA Engineer track). A minimal reference Transfer API (Part A) plus the actual deliverable: a risk-based, automated test effort that would let a bank trust it before shipping (Part B).

## Repo layout

```
src/NovaWallet.Api/         Part A — reference Transfer API (.NET 8 minimal API)
tests/NovaWallet.Tests/     Part B — automated functional/boundary/negative/concurrency/idempotency/security suite (xUnit)
load/                       Stretch goal — basic k6 load smoke test + findings note
.github/workflows/ci.yml    Stretch goal — GitHub Actions: builds and runs the suite on every push
Dockerfile, render.yaml     Deploy config for a free test deployment (see "Deploying" below)
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

Runs from the repo root — the solution file (`NovaWallet.sln`) references both `src/NovaWallet.Api` and `tests/NovaWallet.Tests`, so this one command restores, builds, and runs all 64 tests. No external services, no database, no manually-started server required — the test project boots the real app in-process via `WebApplicationFactory<Program>`. This is exactly what `.github/workflows/ci.yml` runs on every push.

## Running the load smoke test (stretch goal)

See `load/README.md` — requires [k6](https://k6.io) and a running instance of Part A.

## Deploying (free, for testing — not production)

The repo includes a `Dockerfile` and a Render.com Blueprint (`render.yaml`) so Part A can be reached at a public URL without any local setup:

1. Fork or push this repo to your own GitHub account (Render deploys from a repo you own/can grant it access to).
2. Go to [dashboard.render.com](https://dashboard.render.com) → **New** → **Blueprint**, connect the repo. Render reads `render.yaml` automatically and provisions a **free** web service — no credit card required.
3. Render builds the `Dockerfile` and deploys. `Api__BearerToken` is auto-generated as a random secret at deploy time (see the service's **Environment** tab in the Render dashboard to read it back) rather than shipping the same `novawallet-dev-token` default publicly.
4. Once live, hit `https://<your-service>.onrender.com/health` (no auth) to confirm it's up, then use the generated token for everything else:

```bash
curl -X POST https://<your-service>.onrender.com/wallets -H "Authorization: Bearer <token-from-render-dashboard>"
```

**Free-tier caveats, since this is for testing only:** the instance spins down after 15 minutes of inactivity (the first request after that takes ~30-50s to wake it back up), and — same as running it locally — the wallet store is in-memory, so every redeploy or spin-down/spin-up cycle wipes all data back to empty. That's expected for a test deployment of a reference implementation, not a bug.

Verified locally: `dotnet publish` produces the same `NovaWallet.Api.dll` the Docker image runs, and running it with the exact container entrypoint (`ASPNETCORE_URLS` bound to a `$PORT`-style env var, `ASPNETCORE_ENVIRONMENT=Production`) serves `/health` unauthenticated and `/wallets` with the bearer token as expected. The Docker build itself wasn't run in this sandbox (no Docker daemon available here) — Render will run the actual `docker build` on deploy.

## Where to start reading

1. `TEST_STRATEGY.md` — the risk-based plan; read this first.
2. `AI_USAGE.md` — AI usage and judgment calls.
3. `bug-reports/` — issues found, each with severity, repro, and evidence.
4. `EXPLORATORY_TESTING_LOG.md` — the unscripted session and what it turned up.
