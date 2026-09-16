# Load smoke test (stretch goal)

`transfer-load-test.js` is a basic [k6](https://k6.io) smoke test, not a capacity or SLO study. It exists to answer one narrow question — does the API stay correct and reasonably responsive under a moderate burst of concurrent traffic — not to certify a throughput number for production capacity planning. Correctness under concurrency (no double-spend, no negative balance) is already proven with evidence in `tests/NovaWallet.Tests/ConcurrencyTests.cs`; this test's job is throughput and stability, not correctness.

## Running it

```bash
# 1. Start Part A (in a separate terminal, Release build recommended):
dotnet run --project src/NovaWallet.Api -c Release

# 2. Run the smoke test against it:
BASE_URL=http://localhost:5229 k6 run load/transfer-load-test.js
```

`BASE_URL` and `BEARER_TOKEN` are both overridable via environment variables (defaults match the app's own defaults).

## What it does

1. `setup()` creates a pool of 20 wallets and funds each with ₦100,000 (10,000,000 kobo).
2. Ramps from 0 → 20 virtual users over 10s, holds at 20 VUs for 20s, ramps down over 5s (35s total).
3. Each iteration fires one `POST /transfers` of 100 kobo between two random wallets from the pool, each with a unique `Idempotency-Key`.
4. Thresholds: zero HTTP failures (any 5xx) tolerated, and p95 latency on the transfer endpoint under 500ms.

## Findings (this run — an 8 vCPU container, in-memory store, Release build, `dotnet run` directly, no reverse proxy)

- **394,661 requests** over 35s (~20 sustained VUs) — **0 failures**, 100% of checks passed (`status is 201 or 422`, `status is not 5xx`).
- **Throughput:** ~11,257 req/s sustained.
- **Latency:** mean 1.27ms, p90 2.43ms, p95 3.02ms, max 194.92ms (a single outlier, plausibly a GC pause or the very first request paying JIT warm-up cost — not a sustained pattern).
- No negative balances, no crashes, no unhandled exceptions in server logs during the run.

**Caveat:** this is an in-memory, single-process reference implementation with no database, no network hop to a datastore, and no auth-server round-trip — none of which exist yet in Part A. These numbers say "the application logic itself doesn't fall over or corrupt data under load," not "a production deployment would hit 11k req/s." A real deployment (persistent store, real auth, load balancer, multiple instances) would need its own load test against that actual topology before any number here could inform capacity planning.

**What this did *not* test** (would be the next step with more time, per `TEST_STRATEGY.md` §5): sustained load over minutes/hours (memory growth, GC pressure over time), behavior once the in-memory store holds a realistic number of wallets (this run only ever touched a pool of 20), and load concentrated on a small number of "hot" wallets (this run spreads transfers randomly across the pool, which is the easy case for the per-wallet locking scheme — a worst-case test would deliberately funnel most traffic through 1-2 wallets to see how lock contention affects latency under real contention, not just correctness).
