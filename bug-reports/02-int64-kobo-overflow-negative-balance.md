# [BUG-02] Crediting a wallet near `int64.MaxValue` silently overflows to a negative balance

**Severity:** Critical
**Priority:** P0
**Component:** `POST /wallets/{id}/credit`, `POST /transfers` — balance arithmetic in `TransferService`
**Found by:** Targeted probe while doing currency-precision testing (`bug-reports/` hunt after the scripted suite went green), before the exploratory session
**Status:** Fixed (`Services/TransferService.cs`; regression tests: `CurrencyPrecisionTests.Credit_PushingBalancePastInt64Max_IsRejected_NotSilentlyWrapped`, `CurrencyPrecisionTests.Transfer_PushingDestinationBalancePastInt64Max_IsRejected_SourceBalanceUnchanged`)

## Severity/priority rationale

Critical/P0: this is exactly the failure mode the take-home's hard constraint about currency precision is warning against — "money is stored as an integer number of kobo... check for currency precision issues, not just 'amount looks right.'" A wallet balance is `long` (int64) kobo, and by default C# arithmetic is **unchecked**: an addition that exceeds `long.MaxValue` doesn't throw, it silently wraps around to a large *negative* number. The request that triggers it returns `200 OK` — there is no error, no rejection, nothing in the response to suggest anything went wrong. A wallet with a large enough balance ends up with an enormous negative balance, which is the single outcome the brief explicitly says must never happen ("must not allow balance to go negative"). It's astronomically unlikely to happen by accident at realistic Nigerian-Naira wallet balances, but it is trivially reachable by a client that intentionally credits a wallet close to `long.MaxValue` kobo (about ₦92 quadrillion) and credits it again — there's no upper bound check anywhere on `amountKobo` or on the resulting balance.

## Environment

- Commit: pre-fix state of `src/NovaWallet.Api/Services/TransferService.cs` (original `CreditAsync`/`ExecuteTransferAsync`)
- How run: local `dotnet test` against the in-process `WebApplicationFactory<Program>` host, in-memory store

## Repro steps

```csharp
var wallet = await client.CreateWalletAsync();
await client.CreditWalletAsync(wallet.Id, long.MaxValue - 500); // 9223372036854775307

var response = await client.CreditRawAsync(wallet.Id, new { amountKobo = 1_000 });
var final = await client.GetWalletAsync(wallet.Id);
```

Equivalently via `curl` against a running instance:

```bash
curl -X POST http://localhost:5299/wallets/<id>/credit \
  -H "Authorization: Bearer novawallet-dev-token" -H "Content-Type: application/json" \
  -d '{"amountKobo": 9223372036854775307}'

curl -X POST http://localhost:5299/wallets/<id>/credit \
  -H "Authorization: Bearer novawallet-dev-token" -H "Content-Type: application/json" \
  -d '{"amountKobo": 1000}'
```

## Expected result

The second credit either succeeds with a correct, non-negative balance, or — since the true sum exceeds what an `int64` kobo balance can represent — is rejected with `400 Bad Request` and a clear error, leaving the wallet's balance unchanged at its prior (valid) value.

## Actual result

The second credit returned `200 OK`, and the wallet's balance became **`-9223372036854775309`** — a large negative number, silently, with no indication of failure anywhere in the response.

```
after first credit:  9223372036854775307
second credit status: OK
final balance:        -9223372036854775309
```

## Root cause

`wallet.BalanceKobo += amountKobo;` (in `CreditAsync`) and `toWallet.BalanceKobo += amount;` (in `ExecuteTransferAsync`) are plain C# arithmetic on `long`. The project does not set `<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>`, so these operations run **unchecked** by default — CLR integer overflow wraps around (two's-complement) instead of throwing, per the C# language spec. Nothing else in the code validated that a credit or transfer amount would keep the resulting balance within `int64` range.

## Fix

- `CreditAsync` now performs the addition inside a `checked { }` block and catches `OverflowException`, converting it to the standard `ValidationException` → `400 validation_error` response, leaving the wallet's balance untouched (the `checked` block only contains the single mutating statement, so there's nothing partially applied on failure).
- `ExecuteTransferAsync` explicitly checks `toWallet.BalanceKobo > long.MaxValue - amount` **before** mutating either wallet's balance, and rejects with `400 validation_error` if so. This was deliberately written as a pre-check rather than a `checked`/`catch` around both mutating statements: debiting `fromWallet` and crediting `toWallet` are two separate statements, and if only the second one overflowed inside a shared `checked` block, the first (the debit) would already have executed — money would vanish from the source wallet without ever reaching the destination. Checking first avoids ever starting a mutation that can't be completed safely.
