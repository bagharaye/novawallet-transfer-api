# Bug Reports

One Markdown file per real issue found, named `NN-short-slug.md` (e.g. `01-concurrent-transfer-negative-balance.md`), numbered in the order found. Use `TEMPLATE.md` as the starting point for each.

Every report includes, per the take-home's hard constraints:

- **Severity/priority rationale** — not just a label, but *why* that label (financial impact, likelihood, affected users).
- **Exact repro steps** — runnable, ideally as literal `curl`/HTTP calls or a pointer to the exact automated test that reproduces it.
- **Expected vs. actual result.**
- **Evidence** — response body, log output, or screenshot, inline or linked.

Bugs found in Part A's own implementation are reported the same way as any other bug — the brief explicitly asks for that, and pretending your own scaffold is bug-free would defeat the point of the exercise.

This folder is empty until the automated suite, concurrency/idempotency/security tests, and the exploratory session actually surface issues — reports are added as they're found, not invented in advance.
