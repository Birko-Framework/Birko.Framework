using System;

namespace Birko.Caching.Redis
{
    /// <summary>
    /// Thrown when a cache-scoped delete on a <see cref="RedisCache"/> with **no <c>KeyPrefix</c>** would
    /// match every key in the logical database rather than only this cache's entries.
    ///
    /// <para>
    /// <b>Why this exists (SH-H006).</b> Two doors reached a whole-database delete, and
    /// <c>RedisSettings.KeyPrefix</c> defaults to <c>null</c>, so both were on the default path.
    /// <c>RemoveByPrefixAsync("")</c> scanned <c>"*"</c> and <c>DEL</c>'d every key it found — neither command
    /// is admin-gated, so this door was live on **every** configuration, silently. <c>ClearAsync</c> took an
    /// <c>else</c> branch to <c>FLUSHDB</c>, which is admin-gated: it destroyed the database for a consumer
    /// whose <c>RawConnectionString</c> carries <c>allowAdmin=true</c>, and threw
    /// <c>RedisCommandException</c> for everyone else. Either way the keys at risk belong to siblings that
    /// share the connection by design (<c>Birko.MessageQueue.Redis</c>, <c>Birko.BackgroundJobs.Redis</c>,
    /// the Redis sync stores), so a cache clear dropped queued messages and pending background jobs.
    /// <c>ICache.ClearAsync</c> is documented as removing all entries from *the cache*; the implementation was
    /// wider than its own contract.
    /// </para>
    /// <para>
    /// <b>The prefix, not the pattern, is the unit of ownership — and it is escaped.</b> Redis <c>MATCH</c>
    /// treats <c>* ? [ ]</c> as metacharacters while the read path writes them as literals, so an unescaped
    /// prefix let <c>RemoveByPrefixAsync("*")</c> resolve to the non-empty pattern <c>"**"</c> and match every
    /// key — walking past this guard one character wide. The literal is glob-escaped before the pattern is
    /// built.
    /// </para>
    /// <para>
    /// <b>Why it refuses instead of deleting selectively.</b> An unprefixed cache has no key space of its
    /// own. Its keys are written bare, so they are byte-for-byte indistinguishable from every sibling's, and
    /// two unprefixed caches on one database are literally the same key space — "this cache's entries" is
    /// not a set that exists. Inventing one was considered and rejected: an owned-key index needs a key name
    /// (the very layout change that prefixing already is), costs a round-trip per write, and grows without
    /// bound because Redis expiry does not remove members; and scanning a made-up prefix finds nothing,
    /// turning a clear into a silent no-op that reports success. Refusing is the only answer that is neither
    /// destructive nor a lie.
    /// </para>
    /// <para>
    /// <b>The opt-out is checked before this throws, and it is the first one named.</b> Configure
    /// <c>RedisSettings.KeyPrefix</c> and the cache owns a namespace it can clear precisely — that works on
    /// every configuration and is the fix an operator wants. <see cref="RedisCache.FlushDatabaseAsync"/> is
    /// the second door: it destroys the whole database through an explicitly named method deliberately absent
    /// from <c>ICache</c>, but <c>FLUSHDB</c> is **admin-gated** by StackExchange.Redis, so it additionally
    /// needs <c>allowAdmin=true</c> — which <c>RedisSettings.GetConnectionString()</c> does not emit, so it
    /// means supplying <c>RawConnectionString</c>. The message says so rather than sending an operator to a
    /// door that answers with a second, unrelated exception.
    /// </para>
    /// <para>
    /// Mirrors <c>Birko.Data.Exceptions.WholeTableWriteException</c> — same guard, different backend: a write
    /// whose scope reduces to "everything" is refused at the boundary. Derives from
    /// <see cref="InvalidOperationException"/> for the same reason, so existing
    /// <c>catch (InvalidOperationException)</c> blocks keep working while a host that wants to report this
    /// case distinctly can catch this type first. It is a request-shaped problem — the caller asked for
    /// something wider than they said — not a server fault.
    /// </para>
    /// </summary>
    public class WholeDatabaseDeleteException : InvalidOperationException
    {
        /// <summary>The refused operation — the name of the cache method that would have over-deleted.</summary>
        public string Operation { get; }

        /// <summary>The Redis logical database index the delete would have emptied.</summary>
        public int Database { get; }

        public WholeDatabaseDeleteException(string operation, int database)
            : base($"Refusing {operation} on a RedisCache with no KeyPrefix: it would match every key in "
                 + $"database {database}, not just this cache's entries — including keys written by "
                 + "Birko.MessageQueue.Redis, Birko.BackgroundJobs.Redis and any Redis store sharing this "
                 + "connection. An unprefixed cache cannot tell its own keys apart from theirs. Set "
                 + "RedisSettings.KeyPrefix so the cache owns a namespace it can clear. To empty the whole "
                 + "database deliberately, call RedisCache.FlushDatabaseAsync() — note that FLUSHDB is "
                 + "admin-gated, so it also requires a connection built with allowAdmin=true (supply it via "
                 + "RedisSettings.RawConnectionString; GetConnectionString() does not emit it).")
        {
            Operation = operation;
            Database = database;
        }
    }
}
