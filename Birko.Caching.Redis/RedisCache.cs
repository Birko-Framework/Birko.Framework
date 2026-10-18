using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Caching.Serialization;
using Birko.Redis;
using StackExchange.Redis;

namespace Birko.Caching.Redis;

/// <summary>
/// Redis-backed ICache implementation using StackExchange.Redis.
/// The consuming project must reference the StackExchange.Redis NuGet package.
/// </summary>
public sealed class RedisCache : ICache
{
    private readonly RedisConnectionManager _connectionManager;
    private readonly RedisSettings _settings;
    private readonly TimeSpan _defaultExpiration;
    private readonly bool _ownsConnection;
    private int _disposed;

    /// <summary>
    /// Creates a new RedisCache that owns its connection.
    /// </summary>
    /// <param name="settings">Redis connection settings.</param>
    /// <param name="defaultExpiration">Default expiration for entries without explicit options. Defaults to 5 minutes.</param>
    public RedisCache(RedisSettings settings, TimeSpan? defaultExpiration = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connectionManager = new RedisConnectionManager(settings);
        _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(5);
        _ownsConnection = true;
    }

    /// <summary>
    /// Creates a new RedisCache using a shared connection manager.
    /// </summary>
    /// <param name="connectionManager">A pre-configured connection manager.</param>
    /// <param name="settings">Redis settings (for key prefix configuration).</param>
    /// <param name="defaultExpiration">Default expiration for entries without explicit options. Defaults to 5 minutes.</param>
    public RedisCache(RedisConnectionManager connectionManager, RedisSettings settings, TimeSpan? defaultExpiration = null)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(5);
        _ownsConnection = false;
    }

    public async Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        var db = _connectionManager.GetDatabase();
        var fullKey = GetFullKey(key);
        var value = await db.StringGetAsync(fullKey);

        if (!value.HasValue)
            return CacheResult<T>.Miss();

        // Refresh sliding expiration on hit
        await RefreshSlidingExpirationAsync(db, fullKey);

        return CacheResult<T>.Hit(CacheSerializer.Deserialize<T>((byte[])value!)!);
    }

    public async Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        CacheEntryOptions.Validate(options); // SH-L041: a non-positive TTL is refused on every backend
        ct.ThrowIfCancellationRequested();
        var db = _connectionManager.GetDatabase();
        var fullKey = GetFullKey(key);
        var opts = options ?? new CacheEntryOptions { AbsoluteExpiration = _defaultExpiration };

        var serialized = CacheSerializer.Serialize(value);
        var expiry = GetExpiry(opts);

        // `When.Always` is spelled out rather than left to the default. StackExchange.Redis 2.9 added
        // StringSetAsync(key, value, Expiration, ValueCondition, CommandFlags), whose trailing
        // parameters are optional — so a three-argument call now binds THAT overload, and a
        // `TimeSpan?` does not convert to `Expiration` (only a non-nullable `TimeSpan` does). Naming
        // the fourth argument selects the TimeSpan? overload explicitly and compiles against 2.8 and
        // 2.13 alike, with the semantics unchanged: `When.Always` was the old default (TASK-234).
        await db.StringSetAsync(fullKey, serialized, expiry, When.Always);

        // Store sliding expiration metadata if needed. The absolute cap is persisted as a fixed
        // DEADLINE (unix seconds), not the original window — otherwise every refresh recomputes
        // min(sliding, window) against a constant window and the entry lives forever (CR-H014).
        if (opts.SlidingExpiration.HasValue)
        {
            var absoluteDeadline = opts.AbsoluteExpiration.HasValue
                ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)opts.AbsoluteExpiration.Value.TotalSeconds
                : -1;
            await db.HashSetAsync(GetMetaKey(fullKey), [
                new HashEntry("sliding", opts.SlidingExpiration.Value.TotalSeconds),
                new HashEntry("absoluteDeadline", absoluteDeadline)
            ]);
            await db.KeyExpireAsync(GetMetaKey(fullKey), expiry);
        }
        else
        {
            // A meta hash left by an earlier sliding write of this key would go on driving the new entry's
            // expiry: every read re-expired it to the old sliding window, or deleted it once the old deadline
            // had passed (SH-M040). The policy belongs to the entry, so a non-sliding write removes it.
            await db.KeyDeleteAsync(GetMetaKey(fullKey));
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        var db = _connectionManager.GetDatabase();
        var fullKey = GetFullKey(key);
        await db.KeyDeleteAsync([fullKey, GetMetaKey(fullKey)]);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        var db = _connectionManager.GetDatabase();
        return await db.KeyExistsAsync(GetFullKey(key));
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        CacheEntryOptions.Validate(options); // SH-L041: a non-positive TTL is refused on every backend
        ct.ThrowIfCancellationRequested();
        var result = await GetAsync<T>(key, ct);
        if (result.HasValue)
            return result.Value!;

        // Use Redis SET NX as distributed lock
        var db = _connectionManager.GetDatabase();
        var lockKey = GetFullKey($"__lock:{key}");
        // SH-L048: the lock holds this caller's own token, and only the holder of that token releases it. With a
        // constant value, a factory running past the 30 s expiry let a second caller take the lock, and the first
        // caller's release then deleted the second caller's lock.
        var lockToken = Guid.NewGuid().ToString("N");
        var lockAcquired = await db.StringSetAsync(lockKey, lockToken, TimeSpan.FromSeconds(30), When.NotExists);

        try
        {
            if (lockAcquired)
            {
                // We got the lock — create the value
                var value = await factory(ct);
                await SetAsync(key, value, options, ct);
                return value;
            }
            else
            {
                // Another caller is creating — wait and retry
                await Task.Delay(50, ct);
                result = await GetAsync<T>(key, ct);
                if (result.HasValue)
                    return result.Value!;

                // Fallback: create anyway (lock holder may have failed)
                var value = await factory(ct);
                await SetAsync(key, value, options, ct);
                return value;
            }
        }
        finally
        {
            if (lockAcquired)
                await db.ScriptEvaluateAsync(ReleaseLockScript, [lockKey], [lockToken]);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        // Report the argument the caller actually passed: null and "" both normalise to the same empty scope,
        // and an operator grepping logs for their own call must be able to find it.
        // The refusal names the settings' database, not GetDatabase().Database (what SH-M042 scans): reading that would
        // open the connection, and the refusal happens before any connection is opened.
        var operation = $"{nameof(RemoveByPrefixAsync)}({(prefix is null ? "null" : "\"\"")})";
        var pattern = ResolveOwnedKeyPattern(_settings.KeyPrefix, prefix ?? string.Empty)
            ?? throw new WholeDatabaseDeleteException(operation, _settings.Database);

        await RemoveByPatternAsync(pattern, ct);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        // SH-H006: this used to fall through to FLUSHDB whenever no KeyPrefix was configured — which is the
        // DEFAULT, since RedisSettings.KeyPrefix is an unassigned string?. It targeted every key in the
        // logical database, including the queued messages and pending jobs of the sibling components that
        // share this connection by design. FLUSHDB is admin-gated, so on a settings-built connection it threw
        // instead of flushing; the door that destroyed data silently on EVERY configuration was
        // RemoveByPrefixAsync(""), which scans "*" and DELs — neither command gated. An unprefixed cache has
        // no key space of its own to clear (see WholeDatabaseDeleteException for why inventing one was
        // rejected), so both doors now refuse.
        var pattern = ResolveOwnedKeyPattern(_settings.KeyPrefix, string.Empty)
            ?? throw new WholeDatabaseDeleteException(nameof(ClearAsync), _settings.Database);

        await RemoveByPatternAsync(pattern, ct);
    }

    /// <summary>
    /// Destroys **every key in the configured logical database** (Redis <c>FLUSHDB</c>) — not just this
    /// cache's entries. Keys written by <c>Birko.MessageQueue.Redis</c>, <c>Birko.BackgroundJobs.Redis</c>
    /// and any Redis store sharing the connection go with them.
    ///
    /// <para>
    /// Declared on <see cref="RedisCache"/> and deliberately **not** on <c>ICache</c> (SH-H006): a
    /// cache-shaped contract must not be able to empty a database, so reaching this requires holding the
    /// concrete type and naming the operation. That is the explicit door
    /// <see cref="WholeDatabaseDeleteException"/> points callers at, and it is why a <c>FLUSHDB</c> in a
    /// Redis log now means somebody asked for one.
    /// </para>
    /// <para>
    /// <b>Requires admin mode.</b> StackExchange.Redis gates <c>FLUSHDB</c> behind <c>allowAdmin=true</c>
    /// (measured: <c>Message.IsAdmin</c> is <c>true</c> for <c>FLUSHDB</c> and <c>KEYS</c>, <c>false</c> for
    /// <c>SCAN</c>/<c>DEL</c>), and <see cref="RedisSettings.GetConnectionString"/> never emits it — so on a
    /// settings-built connection this throws <c>RedisCommandException</c> rather than flushing. Supply
    /// <c>allowAdmin=true</c> through <see cref="RedisSettings.RawConnectionString"/> to use it. This
    /// precondition is stated in <see cref="WholeDatabaseDeleteException"/>'s message too, so the guard does
    /// not send an operator to a door that answers with an unrelated exception.
    /// </para>
    /// </summary>
    public async Task FlushDatabaseAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        // The database the cache writes to, on every primary (SH-M042, SH-M043): settings.Database could name
        // another index than the shared connection manager's, and one endpoint left the other shards intact.
        var database = _connectionManager.GetDatabase().Database;
        foreach (var server in _connectionManager.GetServers())
            await server.FlushDatabaseAsync(database);
    }

    /// <summary>
    /// Resolves the <c>SCAN</c> pattern covering the keys this cache owns under <paramref name="prefix"/>, or
    /// <c>null</c> when the pattern would degrade to <c>*</c> — i.e. when the caller's scope cannot be
    /// distinguished from "the entire database".
    ///
    /// <para>
    /// The single condition, checked once for both delete doors (SH-H006): the effective prefix is empty. That
    /// happens only when no <c>KeyPrefix</c> is configured **and** the caller supplied no prefix either — a
    /// configured <c>KeyPrefix</c> always contributes at least <c>"{prefix}:"</c>, and a caller-supplied
    /// prefix bounds the pattern on its own even with no <c>KeyPrefix</c>, so ownership is not in question
    /// there.
    /// </para>
    /// <para>
    /// <b>The literal prefix is glob-escaped, and that is load-bearing, not tidiness.</b> Redis <c>MATCH</c>
    /// treats <c>* ? [ ]</c> as metacharacters while <see cref="GetFullKey"/> writes them as literals, so an
    /// unescaped prefix made the delete side match keys the read side never wrote. Concretely it walked
    /// straight past the guard above: <c>RemoveByPrefixAsync("*")</c> on an unprefixed cache resolved to the
    /// non-null pattern <c>"**"</c>, which matches **every key in the database** — the exact whole-database
    /// delete this method exists to refuse, one character wide. A <c>KeyPrefix</c> of <c>"*"</c> did the same
    /// to <c>ClearAsync</c> via <c>"*:*"</c>, reaching every sibling's namespaced key. Escaping makes the two
    /// sides agree, which is what "prefix" meant all along.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than <c>private</c> so the regression suite can pin the decision table directly
    /// — the same reason <see cref="ComputeRefreshedTtl"/> is internal (CR-L039).
    /// </para>
    /// </summary>
    internal static string? ResolveOwnedKeyPattern(string? keyPrefix, string prefix)
    {
        // Composed through the same helper the read/write path uses. Two copies of the key layout is exactly
        // how the delete side ends up addressing keys the read side never wrote — the bug class this method
        // exists to close, arriving from the other direction.
        var fullPrefix = ComposeKey(keyPrefix, prefix);
        // Emptiness is judged on the raw prefix: escaping can only lengthen it, so checking after would
        // report a bounded scope for an unbounded one.
        return fullPrefix.Length == 0 ? null : $"{EscapeGlob(fullPrefix)}*";
    }

    /// <summary>
    /// Escapes the Redis glob metacharacters <c>\ * ? [ ]</c> so a literal key prefix cannot act as a pattern.
    /// The backslash goes first — escaping it after the others would double-escape the escapes.
    /// </summary>
    internal static string EscapeGlob(string literal)
    {
        if (literal.Length == 0) return literal;

        var sb = new System.Text.StringBuilder(literal.Length);
        foreach (var c in literal)
        {
            if (c is '\\' or '*' or '?' or '[' or ']')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Enumerates keys matching <paramref name="pattern"/> and deletes them in batches. Enumeration goes
    /// through <c>KeysAsync</c>, and the array overload of <c>KeyDeleteAsync</c> cuts the N individual
    /// round-trips inside the enumeration (CR-L040).
    /// <para>
    /// <b>The library picks the command, and only one of its choices works here.</b> <c>KeysAsync</c> uses
    /// <c>SCAN</c> or <c>KEYS</c> "based on the server capabilities" (its own doc), and <c>KEYS</c> is
    /// admin-gated (measured: <c>Message.IsAdmin</c> is <c>true</c>) while <c>SCAN</c> is not — so against a
    /// pre-2.8 server this path throws rather than falling back to a server-blocking scan. Stated rather than
    /// claimed, so a refactor does not "preserve" a guarantee the library never made.
    /// </para>
    /// Callers must resolve the pattern through <see cref="ResolveOwnedKeyPattern"/> first — this method
    /// deletes whatever it is given.
    /// </summary>
    private async Task RemoveByPatternAsync(string pattern, CancellationToken ct)
    {
        var db = _connectionManager.GetDatabase();
        // Scan the index the data path writes to (SH-M042: settings.Database could differ from the shared
        // connection manager's, so the scan found nothing and reported success), on every primary
        // (SH-M043: one endpoint left the keys on every other cluster shard in place).
        foreach (var server in _connectionManager.GetServers())
            await RemoveByPatternAsync(db, server, pattern, ct);
    }

    private static async Task RemoveByPatternAsync(IDatabase db, IServer server, string pattern, CancellationToken ct)
    {
        // A multi-key DEL must stay within one hash slot on a cluster (CROSSSLOT otherwise), so there a batch
        // is split by slot; a standalone server takes the batch whole.
        var cluster = server.ServerType == ServerType.Cluster;
        const int batchSize = 512;
        var batch = new List<RedisKey>(batchSize);
        await foreach (var key in server.KeysAsync(pattern: pattern, database: db.Database))
        {
            ct.ThrowIfCancellationRequested();
            batch.Add(key);
            if (batch.Count >= batchSize)
            {
                await DeleteAsync(db, batch, cluster);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
            await DeleteAsync(db, batch, cluster);
    }

    private static async Task DeleteAsync(IDatabase db, List<RedisKey> keys, bool cluster)
    {
        if (!cluster)
        {
            await db.KeyDeleteAsync(keys.ToArray());
            return;
        }
        foreach (var slot in keys.GroupBy(k => db.Multiplexer.HashSlot(k)))
            await db.KeyDeleteAsync(slot.ToArray());
    }

    private string GetFullKey(string key) => ComposeKey(_settings.KeyPrefix, key);

    /// <summary>
    /// The one place the Redis key layout is written down: <c>"{keyPrefix}:{key}"</c> when a prefix is
    /// configured, the bare key otherwise. Both the read/write path (<see cref="GetFullKey"/>) and the
    /// delete path (<see cref="ResolveOwnedKeyPattern"/>) go through it, so the two cannot drift apart.
    /// Note <c>""</c> is a prefix and <c>null</c> is its absence — <c>RedisSettings</c> distinguishes them.
    /// </summary>
    private static string ComposeKey(string? keyPrefix, string key) =>
        keyPrefix is not null ? $"{keyPrefix}:{key}" : key;

    private static RedisKey GetMetaKey(string fullKey) => $"{fullKey}:__meta";

    private static TimeSpan? GetExpiry(CacheEntryOptions opts)
    {
        if (opts.AbsoluteExpiration.HasValue)
            return opts.AbsoluteExpiration;
        if (opts.SlidingExpiration.HasValue)
            return opts.SlidingExpiration;
        return null;
    }

    private async Task RefreshSlidingExpirationAsync(IDatabase db, string fullKey)
    {
        var metaKey = GetMetaKey(fullKey);
        var sliding = await db.HashGetAsync(metaKey, "sliding");
        if (!sliding.HasValue) return;

        var slidingSeconds = (double)sliding;
        if (slidingSeconds <= 0) return;

        var absolute = await db.HashGetAsync(metaKey, "absoluteDeadline");
        var absoluteDeadline = absolute.HasValue ? (long)absolute : -1;

        var newExpiry = ComputeRefreshedTtl(slidingSeconds, absoluteDeadline, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        if (newExpiry == null)
        {
            // Past the absolute deadline — expire now instead of re-extending.
            await db.KeyDeleteAsync([fullKey, metaKey]);
            return;
        }

        await db.KeyExpireAsync(fullKey, newExpiry.Value);
        await db.KeyExpireAsync(metaKey, newExpiry.Value);
    }

    /// <summary>
    /// Computes the refreshed TTL for a sliding entry, capped by the absolute deadline (CR-H014).
    /// Returns min(sliding, time-remaining-to-deadline); or the full sliding span when there is no
    /// absolute cap; or <c>null</c> when the deadline has already passed (caller should expire the
    /// entry). Because the cap is a fixed deadline, the remaining budget strictly shrinks over
    /// time, so an always-accessed entry can no longer live forever.
    /// </summary>
    internal static TimeSpan? ComputeRefreshedTtl(double slidingSeconds, long absoluteDeadlineUnix, long nowUnix)
    {
        var slidingSpan = TimeSpan.FromSeconds(slidingSeconds);
        if (absoluteDeadlineUnix <= 0)
            return slidingSpan; // no absolute cap

        var remaining = absoluteDeadlineUnix - nowUnix;
        if (remaining <= 0)
            return null; // past the deadline

        var remainingSpan = TimeSpan.FromSeconds(remaining);
        return slidingSpan < remainingSpan ? slidingSpan : remainingSpan;
    }

    /// <summary>Deletes the lock only while it still holds the caller's token (SH-L048).</summary>
    private const string ReleaseLockScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    /// <summary>
    /// Disposes an owned connection once, however many threads call it; every later operation throws
    /// <see cref="ObjectDisposedException"/> (SH-L043).
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ownsConnection)
        {
            _connectionManager.Dispose();
        }
    }
}
