---
area: caching
generated-at: 2f8675abb8c68e81c9cefb5455db97a5425ee369
generated-on: 2026-08-12
sources:
  - ../Birko.Caching.Hybrid/HybridCache.cs
  - ../Birko.Caching.Hybrid/HybridCacheOptions.cs
  - ../Birko.Caching.Redis/Exceptions/WholeDatabaseDeleteException.cs
  - ../Birko.Caching.Redis/RedisCache.cs
  - ../Birko.Caching/Core/CacheEntryOptions.cs
  - ../Birko.Caching/Core/CacheResult.cs
  - ../Birko.Caching/Core/ICache.cs
  - ../Birko.Caching/Memory/MemoryCache.cs
  - ../Birko.Caching/Memory/MemoryCacheEntry.cs
  - ../Birko.Caching/Serialization/CacheSerializer.cs
  - ../Birko.Data.SQL.Caching/Caching/SqlCacheKeyBuilder.cs
  - ../Birko.Data.SQL.Caching/Caching/SqlCacheOptions.cs
  - ../Birko.Data.SQL.Caching/Stores/CachedAsyncDataBaseBulkStore.cs
# FEATURE-014 comes from the triggering task (TASK-117), NOT from the evidence pass.
# The evidence pass structurally cannot run from this aggregator: every `sources:` glob points into a
# sibling repo with its own history, so no task's `pr:` sha resolves under `git show` here (verified —
# TASK-113's `86c8247` is an unknown revision in this repo). Hence derived: false, and
# shaped-by-unresolved is omitted because nothing ran to count. Per-sub-repo spec trees (TASK-131) are
# the fix.
source-commits:   # sibling HEADs when this spec was last written (2026-08-12 14:42:55,
                  # commit 715caf9). Reconstructed 2026-08-16 -- see .map.yml § BASELINE AMNESTY.
  ../Birko.Caching: 26e6a3f
  ../Birko.Caching.Hybrid: 74deeb8
  ../Birko.Caching.Redis: 333ba76
  ../Birko.Data.SQL.Caching: f9bbc81
shaped-by: [FEATURE-014]
shaped-by-derived: false
---

# Cache abstraction, tiering and query-cache decoration

## Purpose

This capability gives the framework a single caching contract (`ICache`) with three interchangeable
implementations — an in-process `MemoryCache`, a distributed `RedisCache`, and a two-tier `HybridCache`
that layers a local L1 in front of a shared L2 — plus one consumer of that contract:
`CachedAsyncDataBaseBulkStore<DB, T>`, a drop-in SQL store subclass that transparently caches read
results and wipes its table's cache entries on every write. Anything in the framework that wants
memoisation, a distributed cache, or read-through query caching for a SQL store depends on this area.
The three backends deliberately do **not** behave identically for null values, type mismatches,
`ClearAsync`, or `CachePriority`, and those divergences are recorded below as first-class behaviour.

## Requirements

### Requirement: Unified cache contract

The system SHALL expose all cache backends through `ICache : IDisposable`, whose surface is exactly
`GetAsync<T>`, `SetAsync<T>`, `RemoveAsync`, `ExistsAsync`, `GetOrSetAsync<T>`, `RemoveByPrefixAsync`
and `ClearAsync`, every one taking an optional trailing `CancellationToken` defaulted to `default`, and
`SetAsync` / `GetOrSetAsync` taking an optional `CacheEntryOptions?` defaulted to `null`.

#### Scenario: Backend substitution

- **Given** consumer code typed against `Birko.Caching.ICache`
- **When** it is handed a `MemoryCache`, a `RedisCache` or a `HybridCache`
- **Then** it compiles and runs unchanged, because all three implement the same seven members plus `Dispose()`

#### Scenario: Options and cancellation are optional at every call site

- **Given** a caller invoking `SetAsync("k", value)` with no options and no token
- **When** the call is made
- **Then** it binds to `SetAsync<T>(string, T, CacheEntryOptions? = null, CancellationToken = default)` and the backend substitutes its own default expiration

### Requirement: Lookup results distinguish "absent" from "present but null"

The system SHALL return `CacheResult<T>` from every lookup, a readonly struct exposing `HasValue` and
`Value?`, constructible only via `CacheResult<T>.Hit(value)` (`HasValue == true`) and
`CacheResult<T>.Miss()` (`HasValue == false`, `Value == default`).

#### Scenario: Miss carries the default value

- **Given** a key that is not present in the cache
- **When** `GetAsync<string>(key)` is awaited
- **Then** the result has `HasValue == false` and `Value == null`

#### Scenario: A cached null is a hit, not a miss

- **Given** a `MemoryCache` where `SetAsync<Foo>("k", null)` has been called
- **When** `GetAsync<Foo>("k")` is awaited
- **Then** the result has `HasValue == true` and `Value == null` — the caller can tell "we looked and there is nothing" apart from "we never looked"

### Requirement: Expiration options model

The system SHALL express entry lifetime through `CacheEntryOptions` carrying a nullable
`AbsoluteExpiration`, a nullable `SlidingExpiration` and a `CachePriority Priority` defaulting to
`CachePriority.Normal`, with static factories `Absolute(ttl)`, `Sliding(window)`,
`AbsoluteAndSliding(ttl, sliding)` and `Default` (which is `Absolute(TimeSpan.FromMinutes(5))`).

#### Scenario: Default options

- **Given** no explicit options
- **When** `CacheEntryOptions.Default` is read
- **Then** it yields `AbsoluteExpiration == 5 minutes`, `SlidingExpiration == null`, `Priority == Normal`

#### Scenario: Both windows can be set together

- **Given** `CacheEntryOptions.AbsoluteAndSliding(TimeSpan.FromHours(1), TimeSpan.FromMinutes(5))`
- **When** the options are inspected
- **Then** `AbsoluteExpiration == 1 hour` and `SlidingExpiration == 5 minutes` are both populated

#### Scenario: Priority enum values

- **Given** the `CachePriority` enum
- **When** its members are enumerated
- **Then** they are `Low`, `Normal`, `High`, `NeverRemove` in that order

### Requirement: MemoryCache expiry evaluation

The system SHALL treat a `MemoryCacheEntry` as expired when `AbsoluteExpiration` is set and
`UtcNow - CreatedAt` is **strictly greater** than it, or when `SlidingExpiration` is set and
`UtcNow - LastAccessedAt` is strictly greater than it; an entry with neither window set never expires.

#### Scenario: Absolute window elapsed

- **Given** an entry created at T0 with `Absolute(1 minute)`
- **When** `IsExpired()` is evaluated at T0 + 61 seconds
- **Then** it returns `true`

#### Scenario: Exactly at the boundary is still live

- **Given** an entry created at T0 with `Absolute(1 minute)`
- **When** `IsExpired()` is evaluated at exactly T0 + 60 seconds
- **Then** it returns `false`, because the comparison is `>` and not `>=`

#### Scenario: Entry with no expiration set

- **Given** `SetAsync("k", v, new CacheEntryOptions())` (both windows null)
- **When** `IsExpired()` is evaluated at any later time
- **Then** it returns `false` and the entry survives every eviction sweep

### Requirement: MemoryCache lazily removes expired entries on read

The system SHALL, on `MemoryCache.GetAsync` and `MemoryCache.ExistsAsync`, remove an entry it finds
expired from the backing `ConcurrentDictionary` and report `Miss()` / `false` respectively — **without
consulting `CachePriority`**, so a `NeverRemove` entry is still evicted by a read once its window has
elapsed.

#### Scenario: Expired entry is dropped by the reader

- **Given** a `MemoryCache` holding an entry whose absolute window has elapsed
- **When** `GetAsync<T>(key)` is awaited
- **Then** the result is a `Miss()` and the key is no longer in the dictionary

#### Scenario: NeverRemove does not survive a read after expiry

- **Given** an entry stored with `new CacheEntryOptions { AbsoluteExpiration = 1.Second(), Priority = CachePriority.NeverRemove }`
- **When** `ExistsAsync(key)` is awaited two seconds later
- **Then** it returns `false` and the entry has been removed, because the read path does not check `Priority`

### Requirement: MemoryCache background eviction honours NeverRemove

The system SHALL run a `Timer`-driven `EvictExpired()` sweep at a configurable interval (constructor
parameter `cleanupInterval`, defaulting to 60 seconds) that removes every expired entry **except** those
whose `Options.Priority == CachePriority.NeverRemove`, returns immediately if the cache has been
disposed, and never touches the per-key lock map.

#### Scenario: Sweep removes an ordinary expired entry

- **Given** a `MemoryCache(TimeSpan.FromMilliseconds(50))` holding an expired `Normal`-priority entry
- **When** the cleanup timer fires
- **Then** the entry is removed from `_entries`

#### Scenario: Sweep skips a NeverRemove entry

- **Given** the same cache holding an expired `NeverRemove` entry
- **When** the cleanup timer fires
- **Then** the entry remains in `_entries` (though a subsequent `GetAsync` will still report a miss and delete it)

#### Scenario: Sweep after disposal is a no-op

- **Given** a `MemoryCache` on which `Dispose()` has been called
- **When** an in-flight timer callback reaches `EvictExpired()`
- **Then** the `_disposed` guard returns immediately and no dictionary is touched

### Requirement: MemoryCache sliding refresh happens only on GetAsync

The system SHALL update `MemoryCacheEntry.LastAccessedAt` to `DateTime.UtcNow` only inside
`MemoryCache.GetAsync` for a non-expired entry; `ExistsAsync`, `SetAsync` on a different key, and the
eviction sweep SHALL NOT extend a sliding window.

#### Scenario: Reads keep a sliding entry alive

- **Given** an entry stored with `Sliding(1 minute)`
- **When** `GetAsync` is called every 30 seconds
- **Then** the entry never expires, because each call rewrites `LastAccessedAt`

#### Scenario: Existence probes do not keep it alive

- **Given** the same entry
- **When** only `ExistsAsync` is called every 30 seconds
- **Then** the entry expires after 60 seconds without access, because `ExistsAsync` does not touch `LastAccessedAt`

### Requirement: MemoryCache stores values untyped and degrades type mismatch to a miss

The system SHALL store `MemoryCacheEntry.Value` as `object?`, and on read SHALL return
`CacheResult<T>.Hit(default!)` when the stored value is `null`, `Hit(typed)` when the stored value
satisfies `is T`, and `Miss()` when it does not — never an `InvalidCastException`.

#### Scenario: Wrong requested type reads as a miss

- **Given** `SetAsync<string>("k", "hello")` on a `MemoryCache`
- **When** `GetAsync<int>("k")` is awaited
- **Then** the result is `Miss()`, because `"hello" is int` is false

#### Scenario: Stored null hits regardless of requested type

- **Given** `SetAsync<Foo>("k", null)`
- **When** `GetAsync<Bar>("k")` is awaited
- **Then** the result is `Hit(default)` — the null branch is taken before the type test, so the mismatch is not detected

#### Scenario: GetOrSetAsync returns null for a cached null

- **Given** a `MemoryCache` where the key holds a stored `null`
- **When** `GetOrSetAsync<Foo>(key, factory)` is awaited
- **Then** the factory is never invoked and `null` is returned from a `Task<Foo>`-typed method (`result.Value!`)

### Requirement: MemoryCache write applies default options when none are given

The system SHALL, in `MemoryCache.SetAsync`, construct the entry with `options ?? CacheEntryOptions.Default`
and unconditionally overwrite any existing entry for that key (indexer assignment, not `TryAdd`).

#### Scenario: No options means five minutes absolute

- **Given** `SetAsync("k", value)` with no options
- **When** the stored entry is inspected
- **Then** its options are `CacheEntryOptions.Default` — a 5-minute absolute window

#### Scenario: Re-set resets the creation time

- **Given** a key already holding an entry created at T0
- **When** `SetAsync` is called again for the same key at T1
- **Then** a brand-new `MemoryCacheEntry` replaces it with `CreatedAt == T1`, restarting the absolute window

### Requirement: MemoryCache stampede protection via reference-counted per-key locks

The system SHALL guard `MemoryCache.GetOrSetAsync` with a per-key `SemaphoreSlim(1, 1)`: it performs an
unlocked read, then acquires a live (`!Removed`) `KeyLock` — looping to create a fresh one if the
instance it obtained is being retired — re-checks the cache after acquiring the semaphore, and only then
invokes the factory and stores the result. `Semaphore.WaitAsync(ct)` is awaited **outside** the `try`, so
the `finally` is entered only once the semaphore has actually been acquired. The lock is released and its
refcount decremented in that `finally`; the last releaser (refcount reaching 0) marks it `Removed`, removes
the exact key/value pair from the map, and disposes the semaphore. The eviction timer SHALL NOT remove locks.

#### Scenario: Concurrent callers run the factory once

- **Given** ten concurrent `GetOrSetAsync("k", factory)` calls on a cold key
- **When** all ten proceed
- **Then** the factory is invoked once, because nine callers find the value on the post-lock double-check

#### Scenario: A retiring lock is not handed out

- **Given** `AcquireKeyLock` obtains a `KeyLock` whose `Removed` flag is already `true` (a concurrent last releaser retired it)
- **When** the monitor is entered
- **Then** the refcount is not incremented and the `while (true)` loop retries `GetOrAdd`, yielding a fresh live lock

#### Scenario: Lock map does not grow unbounded

- **Given** `GetOrSetAsync` has been called for 10 000 distinct keys, all of which acquired their semaphore and completed
- **When** the internal `_locks` map is inspected
- **Then** it is empty, because each lock was retired by its last releaser — whereas a key whose `WaitAsync` was cancelled keeps its lock in the map with `Refs > 0` for the process lifetime

#### Scenario: A different key is not serialised

- **Given** two concurrent `GetOrSetAsync` calls for keys `"a"` and `"b"`
- **When** both factories are slow
- **Then** they run in parallel, because each key gets its own semaphore

### Requirement: MemoryCache prefix removal and clear

The system SHALL remove every key that `StartsWith(prefix, StringComparison.Ordinal)` on
`RemoveByPrefixAsync`, and SHALL clear only the entry dictionary on `ClearAsync` — the per-key lock map
is left intact.

#### Scenario: Ordinal, case-sensitive prefix match

- **Given** keys `"sql:User:1"` and `"SQL:User:1"`
- **When** `RemoveByPrefixAsync("sql:")` is awaited
- **Then** only `"sql:User:1"` is removed

#### Scenario: Empty prefix removes everything

- **Given** a populated `MemoryCache`
- **When** `RemoveByPrefixAsync("")` is awaited
- **Then** every key is removed, because every string starts with the empty string

#### Scenario: Clear leaves in-flight stampede locks alone

- **Given** a `GetOrSetAsync` call currently holding a key lock
- **When** `ClearAsync` is awaited concurrently
- **Then** `_entries` is emptied but `_locks` is untouched, so the in-flight caller still completes and stores its value

### Requirement: MemoryCache cancellation checks

The system SHALL call `ct.ThrowIfCancellationRequested()` on entry to `GetAsync`, `SetAsync`,
`RemoveAsync`, `ExistsAsync`, `RemoveByPrefixAsync` and `ClearAsync`; `GetOrSetAsync` has no entry check
of its own but propagates the token into the inner `GetAsync`, `Semaphore.WaitAsync(ct)`, `factory(ct)`
and `SetAsync`.

#### Scenario: Pre-cancelled token throws

- **Given** an already-cancelled `CancellationToken`
- **When** `GetAsync<T>("k", ct)` is awaited
- **Then** an `OperationCanceledException` is thrown before the dictionary is consulted

#### Scenario: Cancellation while waiting for a stampede lock

- **Given** a caller blocked in `GetOrSetAsync` on `Semaphore.WaitAsync(ct)`
- **When** the token is cancelled
- **Then** the wait throws from *outside* the `try`, so the `finally` never runs: neither `Release()` nor `ReleaseKeyLock` executes and the refcount `AcquireKeyLock` already incremented is never decremented, so that `KeyLock` is never marked `Removed`, never removed from `_locks` and its semaphore is never disposed (unlike `HybridCache`, whose `finally` always runs and tracks an `acquired` flag)

### Requirement: MemoryCache disposal

The system SHALL make `MemoryCache.Dispose()` idempotent via a `volatile bool _disposed` flag, dispose
the cleanup timer, dispose every remaining key semaphore while swallowing `ObjectDisposedException`, and
clear both the lock map and the entry map. Cache operations after disposal are not guarded and continue
to operate on the (now empty) dictionaries.

#### Scenario: Double dispose is safe

- **Given** a `MemoryCache`
- **When** `Dispose()` is called twice
- **Then** the second call returns immediately at the `_disposed` guard

#### Scenario: Already-disposed semaphore does not break disposal

- **Given** a lock whose semaphore was already disposed by its last releaser but which is still visible in the map
- **When** `Dispose()` enumerates the locks
- **Then** the `ObjectDisposedException` is caught and disposal continues to the next lock

#### Scenario: Reads after disposal do not throw

- **Given** a disposed `MemoryCache`
- **When** `GetAsync<T>("k")` is awaited
- **Then** it returns `Miss()` rather than `ObjectDisposedException`, because no operation checks `_disposed`

### Requirement: Redis cache key namespacing

The system SHALL prefix every Redis key with `"{RedisSettings.KeyPrefix}:"` when `KeyPrefix` is
non-null, and use the raw key otherwise; sliding-expiration metadata for a key SHALL live at
`"{fullKey}:__meta"`.

#### Scenario: Prefix applied

- **Given** `RedisSettings { KeyPrefix = "app" }`
- **When** `SetAsync("user:1", v)` is awaited
- **Then** the string is written to Redis key `"app:user:1"` and any metadata to `"app:user:1:__meta"`

#### Scenario: No prefix configured

- **Given** `RedisSettings` with `KeyPrefix == null` (the default)
- **When** `SetAsync("user:1", v)` is awaited
- **Then** the Redis key is exactly `"user:1"`

#### Scenario: Remove drops the metadata key too

- **Given** a sliding entry `"app:user:1"` with its `__meta` hash
- **When** `RemoveAsync("user:1")` is awaited
- **Then** a single `KeyDeleteAsync` deletes both `"app:user:1"` and `"app:user:1:__meta"`

### Requirement: Redis TTL selection

The system SHALL derive the Redis `StringSetAsync` expiry from `CacheEntryOptions` as: the
`AbsoluteExpiration` if set, otherwise the `SlidingExpiration` if set, otherwise `null` (no TTL); and
when no options are supplied SHALL substitute `new CacheEntryOptions { AbsoluteExpiration = _defaultExpiration }`,
where `_defaultExpiration` is the constructor argument or 5 minutes.

#### Scenario: Absolute beats sliding for the initial TTL

- **Given** `AbsoluteAndSliding(1 hour, 5 minutes)`
- **When** the value is written
- **Then** the Redis key is set with a 1-hour TTL and the sliding window is recorded in metadata only

#### Scenario: No options means the backend default

- **Given** `new RedisCache(settings)` (no `defaultExpiration` argument) and `SetAsync("k", v)`
- **When** the write occurs
- **Then** the key is set with a 5-minute TTL

#### Scenario: Neither window set means no TTL

- **Given** `SetAsync("k", v, new CacheEntryOptions())`
- **When** the write occurs
- **Then** `StringSetAsync` is called with a `null` expiry and the key persists indefinitely

### Requirement: Redis sliding expiration is capped by a fixed absolute deadline

The system SHALL, when `SlidingExpiration` is set, persist a `__meta` hash containing `sliding`
(seconds) and `absoluteDeadline` — a **fixed unix-seconds deadline** computed as
`UtcNow + AbsoluteExpiration`, or `-1` when there is no absolute window — and SHALL give that hash the
same TTL as the value. When `SlidingExpiration` is not set the system SHALL neither write nor delete the
hash, so an earlier sliding write's `__meta` survives an overwrite of the same key and goes on governing
the new value's expiration. On each `GetAsync` hit the system SHALL recompute the TTL as
`ComputeRefreshedTtl(sliding, deadline, now)` = the full sliding span when `deadline <= 0`,
`min(sliding, deadline - now)` when the deadline is in the future, and `null` when `deadline - now <= 0`;
on `null` it SHALL delete both the value and metadata keys instead of re-extending.

#### Scenario: Sliding without an absolute cap extends indefinitely

- **Given** `Sliding(5 minutes)` (so `absoluteDeadline == -1`)
- **When** the key is read
- **Then** `ComputeRefreshedTtl(300, -1, now)` returns 5 minutes and both keys are re-expired to 5 minutes

#### Scenario: Refresh is clamped to the remaining deadline budget

- **Given** `AbsoluteAndSliding(10 minutes, 5 minutes)` read 8 minutes after being written
- **When** the sliding refresh runs
- **Then** `ComputeRefreshedTtl(300, deadline, now)` returns 2 minutes — `min(sliding, remaining)` — so the entry cannot outlive the absolute deadline no matter how often it is read

#### Scenario: Reading past the deadline deletes the entry but still serves the value

- **Given** a key whose `absoluteDeadline` has already passed but which is still present in Redis
- **When** `GetAsync` is awaited
- **Then** `RefreshSlidingExpirationAsync` deletes both keys, and `GetAsync` nevertheless returns `Hit(value)` for the value it read before the refresh — one stale read is served

#### Scenario: Non-sliding entries skip the refresh round-trip

- **Given** a key written with `Absolute(5 minutes)` and no `__meta` hash left behind by an earlier sliding write
- **When** `GetAsync` is awaited
- **Then** the `sliding` hash field has no value, `RefreshSlidingExpirationAsync` returns immediately, and the TTL is left alone

#### Scenario: A non-positive stored sliding value is ignored

- **Given** a `__meta` hash whose `sliding` field is `0`
- **When** the refresh runs
- **Then** it returns without changing any TTL

#### Scenario: A previous sliding write's metadata governs a later absolute-only write

- **Given** `SetAsync("k", v, Sliding(5 minutes))` followed by `SetAsync("k", v2, Absolute(1 hour))` on the same key
- **When** the key is read
- **Then** the surviving `__meta` (`sliding == 300`, `absoluteDeadline == -1`) is found, so the refresh re-expires the value to 5 minutes on every read even though the current write asked for a 1-hour absolute window

#### Scenario: A stale past deadline deletes the value just written

- **Given** a `__meta` left by an earlier `AbsoluteAndSliding` write whose `absoluteDeadline` has already passed, and a subsequent `SetAsync("k", v2, Absolute(1 hour))`
- **When** the key is read for the first time after that write
- **Then** `ComputeRefreshedTtl` returns `null` and both the value and the metadata key are deleted, discarding the fresh write

### Requirement: Redis values are JSON-serialised through CacheSerializer

The system SHALL serialise and deserialise distributed cache payloads with `CacheSerializer`, a static
facade over a `SystemJsonSerializer` configured with `JsonNamingPolicy.CamelCase`,
`JsonIgnoreCondition.WhenWritingNull` and `WriteIndented = false`.

#### Scenario: Property names are camel-cased on the wire

- **Given** a POCO with a `FirstName` property
- **When** it is cached in Redis and read back
- **Then** the stored JSON uses `firstName` and the round-trip restores the value

#### Scenario: Null properties are omitted

- **Given** a POCO whose `Description` is null
- **When** it is serialised
- **Then** the `description` member is absent from the JSON payload

#### Scenario: Caching a null value throws

- **Given** a `RedisCache`
- **When** `SetAsync<Foo>("k", null)` is awaited
- **Then** `CacheSerializer.Serialize` reaches `ArgumentNullException.ThrowIfNull(value)` inside `SystemJsonSerializer.SerializeToBytes<T>` and an `ArgumentNullException` propagates — where `MemoryCache` would have stored the null as a legitimate hit

#### Scenario: Reading a payload of the wrong shape throws

- **Given** a Redis key holding a serialised JSON array (e.g. written by a `GetAsync<List<T>>` path)
- **When** `GetAsync<T>(key)` is awaited for a non-collection `T`
- **Then** `JsonSerializer.Deserialize<T>` throws a `JsonException` — where `MemoryCache` would have returned `Miss()`

### Requirement: Redis stampede protection uses a best-effort SET NX lock

The system SHALL, in `RedisCache.GetOrSetAsync`, read first, then attempt `StringSetAsync(lockKey, "1",
TimeSpan.FromSeconds(30), When.NotExists)` on `GetFullKey("__lock:{key}")`. The acquirer SHALL run the
factory, store the result, and delete the lock key in a `finally`. A caller that fails to acquire the
lock SHALL `Task.Delay(50, ct)`, re-read once, and if still a miss SHALL run the factory anyway; such a
caller SHALL NOT delete the lock key.

#### Scenario: The lock holder populates the cache

- **Given** a cold key and a single caller
- **When** `GetOrSetAsync` is awaited
- **Then** the lock is acquired, the factory runs once, the value is stored with the supplied options, and the lock key is deleted

#### Scenario: A loser that finds the value avoids the factory

- **Given** two callers, the second losing the `SET NX` race, and the winner completing within 50 ms
- **When** the loser re-reads after its delay
- **Then** it returns the cached value and never invokes the factory

#### Scenario: A slow lock holder does not prevent duplicate factory work

- **Given** a factory that takes 2 seconds and a second caller that loses the lock race
- **When** the second caller's 50 ms delay elapses and the re-read still misses
- **Then** it invokes the factory itself and writes the result — the lock is advisory only, so N concurrent callers can perform N-1 duplicate factory runs

#### Scenario: A loser never releases someone else's lock

- **Given** a caller whose `lockAcquired` is `false`
- **When** its `finally` block runs
- **Then** `KeyDeleteAsync(lockKey)` is skipped, so the real holder's lock survives

### Requirement: Redis prefix removal scans and deletes in batches

The system SHALL implement `RemoveByPrefixAsync` by enumerating `server.KeysAsync(pattern:
"{fullPrefix}*", database: settings.Database)`, buffering matched keys and issuing a single
`KeyDeleteAsync(RedisKey[])` per 512 keys plus a final partial batch, checking
`ct.ThrowIfCancellationRequested()` once per enumerated key. The pattern SHALL be resolved through
`ResolveOwnedKeyPattern`, so a call whose effective prefix is empty — no `KeyPrefix` **and** no
caller-supplied prefix, the same whole-database delete `ClearAsync` refuses — throws
`WholeDatabaseDeleteException` instead of scanning `"*"`. The literal prefix SHALL be glob-escaped
(`\ * ? [ ]`) before the trailing `*` is appended, so a prefix cannot act as a pattern.

#### Scenario: A glob metacharacter in the prefix is matched literally

- **Given** `RedisSettings` with `KeyPrefix == null` and `RemoveByPrefixAsync("*")`
- **When** the pattern is resolved
- **Then** it is `"\\**"` — keys literally beginning with `*` — and **not** `"**"`, which would match every key in the database and pass the emptiness guard unnoticed

#### Scenario: A glob metacharacter in the KeyPrefix does not widen ClearAsync

- **Given** `RedisSettings { KeyPrefix = "*" }`
- **When** `ClearAsync()` is awaited
- **Then** the pattern is `"\\*:*"`, matching only keys written as `"*:{key}"` — not `"*:*"`, which would reach every colon-containing key including every sibling component's

#### Scenario: Escaping keeps the read and delete sides in agreement

- **Given** a `KeyPrefix` containing `*`, `?`, `[` or `]`
- **When** a key is written via `GetFullKey` and later removed by prefix
- **Then** both address the same key, because `GetFullKey` writes metacharacters as literals and the scan now matches them as literals

#### Scenario: An unprefixed cache refuses an empty prefix

- **Given** `RedisSettings` with `KeyPrefix == null` and `RemoveByPrefixAsync("")`
- **When** it is awaited
- **Then** `WholeDatabaseDeleteException` is thrown, because the pattern would have been `"*"` — this is the second door onto the same delete, not a separate case

#### Scenario: A caller-supplied prefix bounds the pattern without a KeyPrefix

- **Given** `RedisSettings` with `KeyPrefix == null` and `RemoveByPrefixAsync("user:")`
- **When** it is awaited
- **Then** the scan proceeds over `"user:*"` — ownership is not in question, so the guard does not fire

#### Scenario: Batched deletion

- **Given** 1 300 keys matching the prefix
- **When** `RemoveByPrefixAsync(prefix)` is awaited
- **Then** three `KeyDeleteAsync` calls are made — 512, 512 and 276 keys — rather than 1 300 individual round-trips

#### Scenario: Metadata keys are swept with their values

- **Given** sliding entries under the prefix, each with a `"{fullKey}:__meta"` hash
- **When** the prefix is removed
- **Then** the metadata keys also match `"{fullPrefix}*"` and are deleted in the same pass

#### Scenario: Cancellation mid-scan

- **Given** a token cancelled while the SCAN enumeration is in progress
- **When** the next key is yielded
- **Then** `OperationCanceledException` is thrown and any keys already buffered in the current partial batch are **not** deleted

### Requirement: Redis ClearAsync refuses when it cannot scope itself

The system SHALL, in `RedisCache.ClearAsync`, resolve the owned key pattern through
`ResolveOwnedKeyPattern(settings.KeyPrefix, "")` and delete by it; and SHALL throw
`WholeDatabaseDeleteException` when that resolution yields `null` — i.e. when no `KeyPrefix` gives the cache
a namespace of its own, so the pattern would be `"*"`. The refusal SHALL precede any use of the connection,
and SHALL name both deliberate routes in order of applicability: configure `RedisSettings.KeyPrefix` (works on
every configuration), or call `RedisCache.FlushDatabaseAsync` (additionally requires `allowAdmin=true`).

#### Scenario: Prefixed cache clears only its own namespace

- **Given** `RedisSettings { KeyPrefix = "app" }`
- **When** `ClearAsync()` is awaited
- **Then** only keys matching `"app:*"` are deleted and unrelated keys in the same database survive

#### Scenario: Unprefixed cache refuses rather than targeting the database

- **Given** `RedisSettings` with `KeyPrefix == null` (the default) sharing database 0 with other framework components
- **When** `ClearAsync()` is awaited
- **Then** `WholeDatabaseDeleteException` is thrown, no command is sent, and every key in database 0 — including those written by `Birko.MessageQueue.Redis` and `Birko.BackgroundJobs.Redis` — survives

#### Scenario: The refusal happens before the connection is opened

- **Given** an unprefixed `RedisCache` pointed at an unreachable server
- **When** `ClearAsync()` is awaited
- **Then** `WholeDatabaseDeleteException` is thrown rather than a `RedisConnectionException`, because the scope check precedes `GetServer()`

#### Scenario: A cancelled token still wins over the scope check

- **Given** an unprefixed `RedisCache` and a pre-cancelled token
- **When** `ClearAsync(token)` is awaited
- **Then** `OperationCanceledException` is thrown, not `WholeDatabaseDeleteException` — the cancellation check is first

#### Scenario: An empty KeyPrefix is a namespace, not the absence of one

- **Given** `RedisSettings { KeyPrefix = "" }`
- **When** `ClearAsync()` is awaited
- **Then** the pattern is `":*"` and the clear proceeds, because the caller did set a prefix and keys are written as `":{key}"`

### Requirement: FLUSHDB is reachable only through an explicitly named door off the ICache surface

The system SHALL expose `RedisCache.FlushDatabaseAsync(CancellationToken)`, which calls
`server.FlushDatabaseAsync(settings.Database)` and destroys every key in the logical database; and SHALL NOT
declare it on `ICache`, so reaching it requires holding the concrete `RedisCache` type. Because
StackExchange.Redis gates `FLUSHDB` behind `allowAdmin=true` and `RedisSettings.GetConnectionString()` never
emits it, this door SHALL be documented — and named in `WholeDatabaseDeleteException`'s message — as
additionally requiring a connection built with admin mode.

#### Scenario: Deliberate whole-database flush on an admin-enabled connection

- **Given** a caller holding a concrete `RedisCache` whose `RedisSettings.RawConnectionString` carries `allowAdmin=true`
- **When** `FlushDatabaseAsync()` is awaited
- **Then** `FLUSHDB` is issued against `settings.Database`, destroying keys the cache never wrote — which is what the method's name says

#### Scenario: The flush door is admin-gated on a settings-built connection

- **Given** a `RedisCache` built from `RedisSettings` without a `RawConnectionString`
- **When** `FlushDatabaseAsync()` is awaited
- **Then** StackExchange.Redis raises `RedisCommandException` ("not available unless admin mode is enabled"), because `GetConnectionString()` emits no `allowAdmin=true` — so the refusal message names `KeyPrefix` first, as the opt-out that works on every configuration

#### Scenario: A cache-typed consumer cannot empty the database

- **Given** consumer code typed against `ICache`
- **When** it attempts to flush the database
- **Then** no member of the interface can do so; `ClearAsync` is namespace-scoped or refuses, so a `FLUSHDB` in a Redis log means somebody named the operation

### Requirement: Redis connection ownership

The system SHALL create and own a `RedisConnectionManager` when constructed from `RedisSettings`, and
SHALL NOT own one when constructed from an externally supplied `RedisConnectionManager`; `Dispose()`
SHALL be idempotent for sequential calls — guarded by a plain, non-`volatile` `bool _disposed`, unlike
`MemoryCache`'s `volatile` one — and dispose the manager only in the owning case. Every data operation
SHALL address the database index the connection manager captured at *its* construction
(`GetDatabase()`), while `RemoveByPrefixAsync`'s and `ClearAsync`'s SCAN and `FlushDatabaseAsync`'s
`FLUSHDB` SHALL address `RedisSettings.Database` from the settings handed to the cache — two indexes the
shared-manager constructor allows to disagree.

#### Scenario: Owned connection is disposed

- **Given** `new RedisCache(settings)`
- **When** `Dispose()` is called
- **Then** the internally created `RedisConnectionManager` is disposed

#### Scenario: Shared connection survives cache disposal

- **Given** `new RedisCache(sharedManager, settings)`
- **When** `Dispose()` is called
- **Then** `sharedManager` is left open for its other users

#### Scenario: Null arguments are rejected

- **Given** a null `settings` or a null `connectionManager`
- **When** either constructor is invoked
- **Then** an `ArgumentNullException` is thrown for the offending parameter

#### Scenario: The read/write index and the SCAN/FLUSHDB index can disagree

- **Given** `new RedisCache(sharedManager, settings)` where `sharedManager` was built from settings naming database 0 and `settings.Database` is 3
- **When** entries are written and then `RemoveByPrefixAsync(prefix)` is awaited
- **Then** the values were written to database 0 while `KeysAsync(..., database: 3)` enumerates database 3, so the call completes successfully having deleted nothing — and `FlushDatabaseAsync` would `FLUSHDB` database 3, which this cache never wrote to

#### Scenario: Concurrent disposal is not serialised

- **Given** two threads calling `Dispose()` on the same owning `RedisCache` simultaneously
- **When** both read `_disposed` before either writes it
- **Then** both pass the guard and `RedisConnectionManager.Dispose()` is entered twice, because the flag is neither `volatile` nor lock-protected

### Requirement: CachePriority is honoured only by MemoryCache's sweep

The system SHALL apply `CacheEntryOptions.Priority` nowhere except `MemoryCache.EvictExpired()`;
`RedisCache` and `HybridCache`'s L2 path ignore it entirely, and `HybridCache.GetL1Options` merely
copies it onto the derived L1 options.

#### Scenario: NeverRemove has no effect in Redis

- **Given** `SetAsync("k", v, new CacheEntryOptions { AbsoluteExpiration = 1.Minute(), Priority = CachePriority.NeverRemove })` on a `RedisCache`
- **When** the minute elapses
- **Then** Redis expires the key as normal — the priority never reaches the server

#### Scenario: Priority is carried into derived L1 options

- **Given** a `HybridCache` with `L1MaxExpiration` set and requested options with `Priority == High`
- **When** `GetL1Options(requested)` builds the L1 options
- **Then** the returned `CacheEntryOptions.Priority` is `High`

### Requirement: HybridCache read path checks L1 then L2 and backfills L1

The system SHALL, in `HybridCache.GetAsync`, return an L1 hit immediately; otherwise consult L2, return
`Miss()` if L2 misses, and on an L2 hit write the value into L1 using `GetL1Options(null)` — i.e. always
`Absolute(L1DefaultExpiration)` — before returning the L2 result.

#### Scenario: L1 hit short-circuits

- **Given** a key present in L1
- **When** `GetAsync` is awaited
- **Then** L2 is never contacted and the L1 result is returned verbatim

#### Scenario: L2 hit warms L1 with the default L1 window, not the entry's options

- **Given** a key absent from L1 but present in L2, and `HybridCacheOptions { L1DefaultExpiration = 30s }`
- **When** `GetAsync` is awaited
- **Then** the value is written to L1 with `Absolute(30 seconds)` regardless of the TTL the entry has in L2

#### Scenario: The two read paths can give the same key different L1 TTLs

- **Given** the same key fetched once via `GetAsync` and once via `GetOrSetAsync(key, factory, options)` with a non-null `options`
- **When** each backfills L1 after an L2 hit
- **Then** `GetAsync` uses `GetL1Options(null)` while `GetOrSetAsync` uses `GetL1Options(options)`, so the resulting L1 windows differ

### Requirement: HybridCache derives capped L1 entry options

The system SHALL compute L1 options in `GetL1Options(requested)` as follows: when `requested == null`,
return `Absolute(L1DefaultExpiration)` **uncapped**; when `L1MaxExpiration == null`, return the
`requested` instance itself; otherwise return a new `CacheEntryOptions` whose `AbsoluteExpiration` is
`min(requested.AbsoluteExpiration, L1MaxExpiration)` if the request set one and
`min(L1MaxExpiration, L1DefaultExpiration)` if it did not, whose `SlidingExpiration` is
`min(requested.SlidingExpiration, L1MaxExpiration)` if the request set one and `null` if it did not, and
whose `Priority` is copied from the request.

#### Scenario: Requested absolute is capped

- **Given** `L1MaxExpiration = 5 minutes` and `requested = Absolute(1 hour)`
- **When** `GetL1Options(requested)` runs
- **Then** the result has `AbsoluteExpiration == 5 minutes`

#### Scenario: Requested absolute below the cap is preserved

- **Given** `L1MaxExpiration = 5 minutes` and `requested = Absolute(10 seconds)`
- **When** `GetL1Options(requested)` runs
- **Then** the result has `AbsoluteExpiration == 10 seconds`

#### Scenario: A sliding-only request gains an absolute window in L1

- **Given** `L1MaxExpiration = 5 minutes`, `L1DefaultExpiration = 30 seconds` and `requested = Sliding(2 minutes)`
- **When** `GetL1Options(requested)` runs
- **Then** the result has `SlidingExpiration == 2 minutes` **and** `AbsoluteExpiration == 30 seconds` — `min(cap, default)` — even though the caller asked for no absolute bound

#### Scenario: Cap disabled passes the caller's own object through

- **Given** `L1MaxExpiration = null`
- **When** `GetL1Options(requested)` runs
- **Then** the exact `requested` instance is returned, so L1 and L2 share one mutable options object

#### Scenario: Null request bypasses the cap

- **Given** `L1DefaultExpiration = 10 minutes` and `L1MaxExpiration = 5 minutes`
- **When** `GetL1Options(null)` runs
- **Then** it returns `Absolute(10 minutes)` — the `requested == null` branch returns before the cap is applied, so L1 entries can exceed `L1MaxExpiration`

### Requirement: HybridCache write ordering is configurable

The system SHALL, when `HybridCacheOptions.WriteThrough` is `true` (the default), start the L1 write
first, await the L2 write inside a `try`, and always await the L1 task in a `finally` so it is never
left unobserved; and when `WriteThrough` is `false`, await the L2 write first (rethrowing when
`FallbackToL1OnL2Failure` is false) and only then write L1. L2 receives the caller's original options;
L1 receives `GetL1Options(options)`.

#### Scenario: Write-through writes both tiers

- **Given** default options and `SetAsync("k", v, Absolute(1 hour))`
- **When** the call completes
- **Then** L2 holds the entry with a 1-hour window and L1 holds it with the capped `GetL1Options` window

#### Scenario: L1 write is observed even when L2 faults hard

- **Given** `WriteThrough = true`, `FallbackToL1OnL2Failure = false`, and an L2 that throws
- **When** `SetAsync` is awaited
- **Then** the L2 exception propagates, but the `finally` has already awaited the L1 task so it is not an unobserved faulted task

#### Scenario: L2-first ordering tolerates an L2 failure when fallback is on

- **Given** `WriteThrough = false`, `FallbackToL1OnL2Failure = true`, and an L2 that throws
- **When** `SetAsync` is awaited
- **Then** the exception is swallowed and L1 is still populated

#### Scenario: L2-first ordering propagates when fallback is off

- **Given** `WriteThrough = false`, `FallbackToL1OnL2Failure = false`, and an L2 that throws
- **When** `SetAsync` is awaited
- **Then** the exception is rethrown by the `catch when (!FallbackToL1OnL2Failure)` filter and L1 is **not** written

### Requirement: HybridCache tolerates L2 failures according to FallbackToL1OnL2Failure

The system SHALL wrap every L2 interaction in `catch when (_options.FallbackToL1OnL2Failure)` (default
`true`), degrading `GetAsync` to `Miss()`, `ExistsAsync` to `false`, `GetOrSetAsync` to the factory path,
and `RemoveAsync` / `RemoveByPrefixAsync` / `ClearAsync` to awaiting the L1 task only. When the flag is
`false` the L2 exception propagates. The exception filter is unqualified, so it also catches
`OperationCanceledException`.

#### Scenario: Unreachable L2 degrades reads to a miss

- **Given** default options and an L2 whose `GetAsync` throws `RedisConnectionException`
- **When** `HybridCache.GetAsync` is awaited for a key absent from L1
- **Then** it returns `Miss()` and the caller sees no exception

#### Scenario: Fallback disabled surfaces the L2 fault

- **Given** `FallbackToL1OnL2Failure = false` and the same failing L2
- **When** `GetAsync` is awaited
- **Then** the `RedisConnectionException` propagates to the caller

#### Scenario: Cancellation is swallowed as a miss

- **Given** default options and a token cancelled while the L2 `GetAsync` is in flight
- **When** the `OperationCanceledException` reaches the filter
- **Then** it is caught and `Miss()` is returned instead of the cancellation being observed by the caller

#### Scenario: A refusing L2 clear degrades to an L1-only clear

- **Given** default options and an L2 `RedisCache` with no `KeyPrefix`, whose `ClearAsync` throws `WholeDatabaseDeleteException`
- **When** `HybridCache.ClearAsync` is awaited
- **Then** the filter catches it and only L1 is cleared — the L2 entries remain and the caller sees no exception, because the fallback filter is unqualified and does not distinguish a misconfiguration from an outage

#### Scenario: Removal keeps L1 consistent when L2 is down

- **Given** default options and an L2 whose `RemoveAsync` throws
- **When** `HybridCache.RemoveAsync(key)` is awaited
- **Then** the L1 removal is awaited and completes, and no exception escapes — so L1 no longer serves the stale value even though L2 may still hold it

### Requirement: HybridCache existence probe is an OR across tiers

The system SHALL return `true` from `ExistsAsync` if L1 reports the key present, otherwise return L2's
answer (or `false` if L2 fails and fallback is enabled).

#### Scenario: L1 presence is sufficient

- **Given** a key in L1 only
- **When** `ExistsAsync` is awaited
- **Then** it returns `true` without contacting L2

#### Scenario: L2-only presence is reported

- **Given** a key in L2 only
- **When** `ExistsAsync` is awaited
- **Then** it returns `true` and, unlike `GetAsync`, L1 is **not** backfilled

### Requirement: HybridCache stampede protection with reference-counted locks

The system SHALL guard `HybridCache.GetOrSetAsync` with a per-key `SemaphoreSlim(1, 1)` held in a plain
`Dictionary` behind a `_locksGate` monitor, incrementing `RefCount` on entry and, in the `finally`,
releasing the semaphore **only if it was actually acquired** and disposing/removing the lock when
`--RefCount == 0`. After acquiring the lock it SHALL re-check **L1 only** before invoking the factory,
then store the value via its own `SetAsync` (writing both tiers).

#### Scenario: Concurrent callers run the factory once

- **Given** ten concurrent `GetOrSetAsync` calls on a key absent from both tiers
- **When** all ten proceed
- **Then** the first runs the factory and writes through to L1 and L2, and the other nine return the value found by the post-lock L1 re-check

#### Scenario: Cancellation before acquisition does not over-release

- **Given** a caller whose `Semaphore.WaitAsync(ct)` throws because the token was cancelled
- **When** the `finally` runs
- **Then** `acquired` is `false` so `Release()` is skipped, and only the refcount bookkeeping happens

#### Scenario: Lock map is bounded

- **Given** `GetOrSetAsync` has been called for many distinct keys, all completed
- **When** `_locks` is inspected
- **Then** it is empty, because the last releaser for each key removes and disposes the lock

#### Scenario: Post-lock re-check does not consult L2

- **Given** another node populated L2 for this key between this caller's pre-lock L2 probe and its acquisition of the lock
- **When** the post-lock re-check runs
- **Then** only L1 is examined, the re-check misses, and the factory runs even though L2 now holds a value

### Requirement: HybridCache does not own its tiers

The system SHALL reject null `l1` or `l2` at construction with `ArgumentNullException`, default
`options` to `new HybridCacheOptions()`, and in `Dispose()` dispose only its own key semaphores —
**never** L1 or L2, whose lifetime belongs to the caller.

#### Scenario: Tiers survive the hybrid's disposal

- **Given** a `HybridCache` over a shared `MemoryCache` L1 and `RedisCache` L2
- **When** the hybrid is disposed
- **Then** both tiers remain usable by other holders

#### Scenario: Null tier rejected

- **Given** `new HybridCache(null, l2)`
- **When** the constructor runs
- **Then** an `ArgumentNullException` naming `l1` is thrown

#### Scenario: Disposal is idempotent

- **Given** a `HybridCache`
- **When** `Dispose()` is called twice
- **Then** the second call returns at the `_disposed` guard without re-disposing semaphores — a plain, non-`volatile` `bool`, so this holds for sequential calls only

### Requirement: SQL query cache keys are deterministic and table-scoped

The system SHALL build SQL query cache keys as
`"sql:{tableName}:{filterHash}:{orderHash}:{limit}:{offset}"`, where each hash is the first 8 bytes of
the SHA-256 of the corresponding string rendered as 16 lowercase hex characters, and where a null or
empty filter string, order string, limit or offset is rendered as the literal `"_"`. The invalidation
prefix for a table SHALL be `"sql:{tableName}:"`.

#### Scenario: All components absent

- **Given** `BuildKey("User", null, null, null, null)`
- **When** the key is built
- **Then** it is exactly `"sql:User:_:_:_:_"`

#### Scenario: Same inputs, same key

- **Given** two calls with identical table, filter string, order string, limit and offset
- **When** both keys are built
- **Then** they are byte-identical, because SHA-256 is deterministic and no salt or timestamp is involved

#### Scenario: Prefix covers every key for a table

- **Given** any key produced by `BuildKey("User", ...)`
- **When** it is tested against `GetTablePrefix("User")`
- **Then** it starts with `"sql:User:"`, so a single `RemoveByPrefixAsync` invalidates all of that table's cached queries

#### Scenario: Table names sharing a prefix are not isolated

- **Given** tables `"User"` and `"UserRole"`
- **When** `GetTablePrefix("User")` is used for invalidation
- **Then** `"sql:UserRole:..."` keys do **not** match, because the prefix ends with the `:` separator

### Requirement: Cached SQL store memoises both read paths

The system SHALL override both `ReadCoreAsync` overloads of `AsyncDataBaseBulkStore<DB, T>` in
`CachedAsyncDataBaseBulkStore<DB, T>`: the single-result overload keys on
`BuildKey(table, filter?.ToString(), null, 1, null)` and caches a `T`, and the collection overload keys
on `BuildKey(table, filter?.ToString(), orderString, limit, offset)` — where `orderString` is
`orderBy?.ToDictionary()` joined as `"{key}:{value}"` pairs separated by commas, or null — materialises
the base result with `ToList()` and caches a `List<T>`. Both write with
`CacheEntryOptions.Absolute(SqlCacheOptions.DefaultExpiration)` (5 minutes by default).

#### Scenario: Second identical read is served from cache

- **Given** a `CachedAsyncDataBaseBulkStore` over a `MemoryCache`
- **When** `ReadAsync(x => x.Active, orderBy, 10, 0)` is awaited twice with no intervening write
- **Then** the second call returns the cached `List<T>` and the database is queried once

#### Scenario: Order clause participates in the key

- **Given** the same filter, limit and offset but two different `OrderBy<T>` instances
- **When** both reads run
- **Then** they produce different `orderHash` components and are cached separately

#### Scenario: A cached null list degrades to an empty sequence

- **Given** a cache hit whose `Value` is null
- **When** the collection overload returns
- **Then** it yields `Enumerable.Empty<T>()` rather than null

#### Scenario: Single-read and limit-1 collection read collide on one key

- **Given** `ReadCoreAsync(filter)` (single) and `ReadCoreAsync(filter, orderBy: null, limit: 1, offset: null)` (collection) for the same filter
- **When** both keys are built
- **Then** they are the identical string `"sql:{table}:{filterHash}:_:1:_"`, so the two paths overwrite one another's entries even though one stores a `T` and the other a `List<T>`

### Requirement: Cached SQL store caches negative single-result reads

The system SHALL store the result of the single-result `ReadCoreAsync` even when the base store returned
`null`, calling `_cache.SetAsync(key, result, ...)` unconditionally after the base read.

#### Scenario: Miss is remembered on a memory-backed cache

- **Given** a `CachedAsyncDataBaseBulkStore` over a `MemoryCache` and a filter matching no row
- **When** the single-result read is awaited twice
- **Then** the null is cached as a hit, the second call returns null without querying the database, and the negative result persists for the configured expiration

#### Scenario: Miss caching faults on a Redis-backed cache

- **Given** the same store over a `RedisCache` and a filter matching no row
- **When** the single-result read is awaited
- **Then** the base read returns null, `SetAsync` reaches `CacheSerializer.Serialize(null)`, and an `ArgumentNullException` propagates out of `ReadCoreAsync`

### Requirement: Cached SQL store invalidates the whole table on every write

The system SHALL invalidate by calling `_cache.RemoveByPrefixAsync(SqlCacheKeyBuilder.GetTablePrefix(table))`
after the base call in each of: `CreateCoreAsync(T)`, `UpdateCoreAsync(T)`, `DeleteCoreAsync(T)`,
`CreateCoreAsync(IEnumerable<T>)`, `UpdateCoreAsync(IEnumerable<T>)`, `DeleteCoreAsync(IEnumerable<T>)`,
and — because they bypass the `*Core` template and issue SQL straight through the connector — the public
`UpdateAsync(Expression<Func<T,bool>>, PropertyUpdate<T>)` and `DeleteAsync(Expression<Func<T,bool>>)`
overrides.

#### Scenario: A single-entity create clears cached queries

- **Given** cached results for three different filters on table `User`
- **When** `CreateAsync(user)` is awaited
- **Then** all three entries are removed by the `"sql:User:"` prefix sweep and the next read re-queries the database

#### Scenario: A native filter update also invalidates

- **Given** cached results for table `User`
- **When** `UpdateAsync(x => x.Active, new PropertyUpdate<T>(...))` is awaited
- **Then** the override runs the base native `UPDATE ... SET` and then invalidates the table prefix, so no stale read survives to TTL expiry

#### Scenario: Invalidation is unconditional on failure-free completion only

- **Given** a base write that throws
- **When** the override awaits it
- **Then** the exception propagates before `InvalidateCacheAsync` is reached and the cache is left untouched

#### Scenario: Invalidation is coarse

- **Given** a cached read filtered to `TenantGuid == A` and an update touching only tenant B's rows
- **When** the update completes
- **Then** tenant A's cached entry is removed as well, because invalidation is per-table and never per-key

### Requirement: SQL caching can be disabled at runtime

The system SHALL, when `SqlCacheOptions.Enabled` is `false`, delegate both `ReadCoreAsync` overloads
straight to `base` without touching the cache, and make `InvalidateCacheAsync` a no-op that returns
before computing a prefix; the write overrides still call `base`.

#### Scenario: Disabled reads bypass the cache entirely

- **Given** `new SqlCacheOptions { Enabled = false }`
- **When** any read is awaited
- **Then** no key is built, `_cache` is never called, and the base store's result is returned directly

#### Scenario: Disabled writes leave a shared cache stale

- **Given** two stores over the same `ICache` and the same table, one with `Enabled = false` and one with `Enabled = true`
- **When** the disabled store performs a write
- **Then** `InvalidateCacheAsync` returns early, so the enabled store keeps serving its now-stale cached reads until TTL expiry

### Requirement: Cached SQL store resolves its table name once at construction

The system SHALL resolve `_tableName` in the constructor via `SQL.DataBase.LoadTable(typeof(T))`,
falling back to `typeof(T).Name` when no table is found or its `Name` is empty, and SHALL reject a null
`cache` argument with `ArgumentNullException` while defaulting a null `options` to
`new SqlCacheOptions()`.

#### Scenario: Mapped table name is used

- **Given** `T` decorated with a table attribute naming `"tbl_user"`
- **When** the store is constructed
- **Then** cache keys and the invalidation prefix use `"tbl_user"`

#### Scenario: Unmapped type falls back to the CLR type name

- **Given** `LoadTable(typeof(T))` returns null
- **When** the store is constructed
- **Then** `_tableName` is `typeof(T).Name`

#### Scenario: Null cache rejected

- **Given** `new CachedAsyncDataBaseBulkStore<DB, T>(null)`
- **When** the constructor runs
- **Then** an `ArgumentNullException` naming `cache` is thrown

#### Scenario: Keys carry no connection identity

- **Given** two `CachedAsyncDataBaseBulkStore<DB, T>` instances configured (via `SetSettings`) against two different databases but sharing one `ICache`
- **When** each caches a read for the same filter
- **Then** both compute the same key, because `ResolveTableName()` depends only on `T`'s mapping and the key format has no database, connection or tenant component — each store can serve the other's rows
