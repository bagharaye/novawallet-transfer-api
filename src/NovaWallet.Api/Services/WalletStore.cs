using System.Collections.Concurrent;
using NovaWallet.Api.Models;

namespace NovaWallet.Api.Services;

/// <summary>
/// In-memory storage for wallets, plus the per-key async locks that make
/// transfers and idempotency-key handling atomic. A real deployment would
/// use a database transaction instead of an in-process lock; this reference
/// implementation is explicitly not production-grade (see README).
/// </summary>
public sealed class WalletStore
{
    private readonly ConcurrentDictionary<string, Wallet> _wallets = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _walletLocks = new();
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _idempotencyRecords = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _idempotencyLocks = new();

    public Wallet Add(Wallet wallet) => _wallets[wallet.Id] = wallet;

    public bool TryGet(string id, out Wallet wallet) => _wallets.TryGetValue(id, out wallet!);

    public SemaphoreSlim GetWalletLock(string id) =>
        _walletLocks.GetOrAdd(id, static _ => new SemaphoreSlim(1, 1));

    public SemaphoreSlim GetIdempotencyLock(string key) =>
        _idempotencyLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));

    public bool TryGetIdempotencyRecord(string key, out IdempotencyRecord record) =>
        _idempotencyRecords.TryGetValue(key, out record!);

    public void SaveIdempotencyRecord(string key, IdempotencyRecord record) =>
        _idempotencyRecords[key] = record;
}

public sealed record IdempotencyRecord(string RequestHash, int StatusCode, string ResponseBodyJson);
