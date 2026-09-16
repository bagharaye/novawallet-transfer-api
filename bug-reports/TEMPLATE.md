# [BUG-NN] Short, specific title

**Severity:** Critical / High / Medium / Low
**Priority:** P0 / P1 / P2 / P3
**Component:** e.g. `POST /transfers`, idempotency handling, daily-limit reset
**Found by:** Automated suite (`<test name>`) / Exploratory session / Manual probe
**Status:** Open

## Severity/priority rationale

Why this severity, specifically — what's the financial/security/regulatory/UX impact if this ships, and how likely is it to be hit in practice? (E.g. "Critical/P0 — allows a wallet balance to go negative under concurrent load, i.e. the bank loses money on every occurrence, and any two near-simultaneous transfer requests from a normal client can trigger it.")

## Environment

- Commit/branch: `<sha>`
- How run: e.g. local `dotnet run`, in-memory store, etc.

## Repro steps

1. ...
2. ...
3. ...

Prefer literal, runnable steps (`curl` commands, or a reference to the exact automated test file/case that reproduces it deterministically) over prose description.

## Expected result

## Actual result

## Evidence

Response bodies, log excerpts, or screenshots. Use fenced code blocks for text evidence.

## Suggested fix / notes (optional)
