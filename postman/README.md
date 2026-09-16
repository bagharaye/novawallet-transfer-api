# Postman collection

`NovaWallet.postman_collection.json` is a ready-to-run collection covering every endpoint's happy path plus the boundary, negative, idempotency, daily-limit, and security cases from `TEST_STRATEGY.md` — with assertions built in, not just requests to poke at manually.

## Import

1. Postman → **Import** → select `NovaWallet.postman_collection.json`.
2. Check the collection's **Variables** tab:
   - `baseUrl` defaults to the deployed Render test instance (`https://novawallet-transfer-api.onrender.com`) — change it to `http://localhost:5229` (or whatever `dotnet run` prints) to test locally instead.
   - `token` defaults to `novawallet-dev-token`. If the deployment has been locked down with its own generated token (see the main `README.md`'s "Deploying" section), update this value.
3. First request is a live Render instance on the free tier — if it's been idle, the first request can take 30-50s to wake it up. That's normal, not a bug.

## Running it

- **Whole collection, top to bottom:** click the collection → **Run**. Folder `1. Setup` creates two wallets and captures their IDs into collection variables (`walletA`, `walletB`) that every later request reuses automatically — no manual copy-pasting of wallet IDs between requests.
- **Individual folders/requests:** fine too, as long as `1. Setup` has been run at least once in that session so `walletA`/`walletB` are populated.
- Every request has a **Tests** tab with `pm.test(...)` assertions (status code, and for a few key ones, response-body shape) — the Postman **Test Results** tab shows pass/fail per request, so a full collection run doubles as a quick manual smoke check.

## What's covered

| Folder | What it exercises |
|---|---|
| 1. Setup | Wallet creation, crediting |
| 2. Wallets | `GET /wallets/{id}` happy path + 404, negative/fractional credit amounts rejected |
| 3. Transfers | Happy path, same-key idempotent replay, same-key-different-payload conflict (409), same-wallet rejection, insufficient funds (422), fractional amount rejected, unknown wallet (404) |
| 4. Daily Limit | A transfer that clears the balance check but exceeds the ₦500,000/day outbound limit (422, `daily_limit_exceeded`) |
| 5. Security | Missing token, wrong token (both 401), an injection-like wallet ID (404, not a server error) |
| 6. Health | Unauthenticated `/health` |

This is the exploratory/manual counterpart to the automated `dotnet test` suite in `tests/NovaWallet.Tests` — same scenarios, different tool, useful for demoing or poking at the live deployment without writing C#.
