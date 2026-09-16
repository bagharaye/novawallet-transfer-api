# Exploratory Testing Session Log

**Time-box:** ≤ 60 minutes
**Status:** Not yet run — this session happens after the scripted automated suite (functional, currency, idempotency, concurrency, limit/WAT, security) is green, per `TEST_STRATEGY.md` §2. Exploratory testing is most valuable once the known/scripted issues are already cleared out, so it can spend its budget on what the checklist wouldn't think to ask.

## Charter

Explore the NovaWallet Transfer API's endpoints (`POST /wallets`, `POST /wallets/{id}/credit`, `POST /transfers`, `GET /wallets/{id}`) looking for behavior that a scripted checklist wouldn't catch: surprising interactions between features (e.g. crediting a wallet mid-transfer, rapid-fire sequences across multiple wallets), error-message content that leaks internal detail, inconsistent status codes/response shapes across similar failure cases, and anything that "feels wrong" even if no single assertion was planned to catch it.

## Session log

| Time | Action taken | Observation | Bug filed? |
|---|---|---|---|
| _(TBD)_ | | | |

_(Filled in live during the actual timed session — one row per notable probe, not a transcript of every request.)_

## Summary

- Duration actually used:
- Number of issues found:
- Bug reports filed: see `bug-reports/`
