using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NovaWallet.Api.Errors;
using NovaWallet.Api.Models;
using NovaWallet.Api.Options;

namespace NovaWallet.Api.Services;

public sealed class TransferService(WalletStore store, IClock clock, IOptions<ApiOptions> apiOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApiOptions _options = apiOptions.Value;

    public Wallet CreateWallet()
    {
        var wallet = new Wallet
        {
            Id = Guid.NewGuid().ToString("N"),
            BalanceKobo = 0,
            CreatedAt = clock.UtcNow,
            DailyLimitWatDate = WestAfricaTime.CalendarDate(clock.UtcNow),
        };
        return store.Add(wallet);
    }

    public Wallet GetWallet(string walletId)
    {
        if (!store.TryGet(walletId, out var wallet))
        {
            throw new WalletNotFoundException(walletId);
        }

        return wallet;
    }

    public async Task<Wallet> CreditAsync(string walletId, JsonElement? amountKoboRaw)
    {
        if (!AmountParsing.TryGetKobo(amountKoboRaw, out var amountKobo, out var error))
        {
            throw new ValidationException(error!);
        }

        if (amountKobo <= 0)
        {
            throw new ValidationException("amountKobo must be a positive integer number of kobo.");
        }

        if (!store.TryGet(walletId, out var wallet))
        {
            throw new WalletNotFoundException(walletId);
        }

        var walletLock = store.GetWalletLock(walletId);
        await walletLock.WaitAsync();
        try
        {
            try
            {
                checked
                {
                    wallet.BalanceKobo += amountKobo;
                }
            }
            catch (OverflowException)
            {
                throw new ValidationException(
                    $"Crediting {amountKobo} kobo would overflow wallet '{walletId}' balance.");
            }

            return wallet;
        }
        finally
        {
            walletLock.Release();
        }
    }

    public async Task<(TransferResponse Response, bool WasReplayed)> TransferAsync(
        TransferRequest request, string? idempotencyKey)
    {
        ValidateShape(request, out var fromId, out var toId, out var amount);

        if (idempotencyKey is null)
        {
            var response = await ExecuteTransferAsync(fromId, toId, amount);
            return (response, false);
        }

        var requestHash = HashRequest(fromId, toId, amount);
        var idemLock = store.GetIdempotencyLock(idempotencyKey);
        await idemLock.WaitAsync();
        try
        {
            if (store.TryGetIdempotencyRecord(idempotencyKey, out var existing))
            {
                if (existing.RequestHash != requestHash)
                {
                    throw new IdempotencyKeyConflictException(idempotencyKey);
                }

                var cached = JsonSerializer.Deserialize<TransferResponse>(existing.ResponseBodyJson, JsonOptions)!;
                return (cached, true);
            }

            var response = await ExecuteTransferAsync(fromId, toId, amount);
            var record = new IdempotencyRecord(requestHash, StatusCodes.Status201Created,
                JsonSerializer.Serialize(response, JsonOptions));
            store.SaveIdempotencyRecord(idempotencyKey, record);
            return (response, false);
        }
        finally
        {
            idemLock.Release();
        }
    }

    private async Task<TransferResponse> ExecuteTransferAsync(string fromId, string toId, long amount)
    {
        if (!store.TryGet(fromId, out var fromWallet))
        {
            throw new WalletNotFoundException(fromId);
        }

        if (!store.TryGet(toId, out var toWallet))
        {
            throw new WalletNotFoundException(toId);
        }

        // Always acquire the two wallet locks in a fixed, id-based order so that two
        // transfers moving money in opposite directions between the same pair of
        // wallets can never deadlock on each other's lock.
        var (firstId, secondId) = string.CompareOrdinal(fromId, toId) < 0 ? (fromId, toId) : (toId, fromId);
        var firstLock = store.GetWalletLock(firstId);
        var secondLock = store.GetWalletLock(secondId);

        await firstLock.WaitAsync();
        try
        {
            await secondLock.WaitAsync();
            try
            {
                if (fromWallet.BalanceKobo < amount)
                {
                    throw new InsufficientFundsException(fromId);
                }

                var today = WestAfricaTime.CalendarDate(clock.UtcNow);
                if (fromWallet.DailyLimitWatDate != today)
                {
                    fromWallet.DailyLimitWatDate = today;
                    fromWallet.DailyOutboundUsedKobo = 0;
                }

                if (fromWallet.DailyOutboundUsedKobo + amount > _options.DailyOutboundLimitKobo)
                {
                    throw new DailyLimitExceededException(fromId, _options.DailyOutboundLimitKobo);
                }

                // Checked for overflow *before* mutating either wallet: applying the debit
                // and credit as two separate checked statements would risk leaving the
                // debit applied and the credit unapplied if only the second one overflowed
                // — money debited from fromWallet but never credited to toWallet.
                if (toWallet.BalanceKobo > long.MaxValue - amount)
                {
                    throw new ValidationException(
                        $"Transferring {amount} kobo would overflow destination wallet '{toId}' balance.");
                }

                fromWallet.BalanceKobo -= amount;
                toWallet.BalanceKobo += amount;
                fromWallet.DailyOutboundUsedKobo += amount;

                return new TransferResponse(
                    Id: Guid.NewGuid().ToString("N"),
                    FromWalletId: fromId,
                    ToWalletId: toId,
                    AmountKobo: amount,
                    CreatedAt: clock.UtcNow);
            }
            finally
            {
                secondLock.Release();
            }
        }
        finally
        {
            firstLock.Release();
        }
    }

    private static void ValidateShape(TransferRequest request, out string fromId, out string toId, out long amount)
    {
        if (string.IsNullOrWhiteSpace(request.FromWalletId))
        {
            throw new ValidationException("fromWalletId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ToWalletId))
        {
            throw new ValidationException("toWalletId is required.");
        }

        if (request.FromWalletId == request.ToWalletId)
        {
            throw new ValidationException("fromWalletId and toWalletId must be different wallets.");
        }

        if (!AmountParsing.TryGetKobo(request.AmountKobo, out amount, out var error))
        {
            throw new ValidationException(error!);
        }

        if (amount <= 0)
        {
            throw new ValidationException("amountKobo must be a positive integer number of kobo.");
        }

        fromId = request.FromWalletId;
        toId = request.ToWalletId;
    }

    private static string HashRequest(string fromId, string toId, long amount)
    {
        var canonical = $"{fromId}|{toId}|{amount}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }
}
