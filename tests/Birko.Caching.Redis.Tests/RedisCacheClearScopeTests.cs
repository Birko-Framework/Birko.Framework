using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Caching;
using Birko.Caching.Redis;
using Birko.Redis;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Birko.Caching.Redis.Tests;

/// <summary>
/// Regression for SH-H006 (TASK-117): <c>RedisCache.ClearAsync</c> fell through to
/// <c>server.FlushDatabaseAsync</c> whenever no <c>KeyPrefix</c> was configured — and
/// <c>RedisSettings.KeyPrefix</c> is an unassigned <c>string?</c>, so the destructive branch was the
/// <b>default</b> one. It destroyed every key in the logical database, not just the cache's entries,
/// including the queued messages and pending jobs of the siblings that share the connection by design
/// (<c>Birko.MessageQueue.Redis</c>, <c>Birko.BackgroundJobs.Redis</c>, the Redis sync stores), and reported
/// success. <c>ICache.ClearAsync</c> promises to clear *the cache*; the implementation was wider than its
/// own contract.
///
/// <para>
/// <c>RemoveByPrefixAsync("")</c> reached the identical whole-database delete through a second door: with no
/// <c>KeyPrefix</c> the effective prefix is empty, so the scan pattern was <c>"*"</c>. Same root cause — an
/// unprefixed cache writes bare keys and therefore owns no distinguishable key space — so both doors refuse.
/// </para>
/// <para>
/// <b>Asserted offline, and that is the stronger assertion.</b> The guard runs before the connection is
/// touched, so there is nothing to observe on a live server: the proof that other components' keys survive is
/// that no command is ever sent. These tests rely on the same lazy-connection property the CR-M034
/// cancellation suite does, so no Redis (and no STORY-042 Docker gate) is required.
/// </para>
/// </summary>
public class RedisCacheClearScopeTests
{
    /// <summary>
    /// TEST-NET-1 (RFC 5737) — reserved for documentation and guaranteed never routable — with a short
    /// connect timeout.
    /// <para>
    /// <b>This is not paranoia; the first version of this file was destructive.</b> The tests that assert a
    /// call is *not* refused necessarily run past the guard and reach <c>GetDatabase()</c>/<c>GetServer()</c>.
    /// Pointed at <c>localhost:6379</c> they issued real <c>SCAN test:*</c> + <c>DEL</c> and
    /// <c>SCAN user:*</c> + <c>DEL</c> against database 0 — so on any developer box with a local Redis, the
    /// regression suite for a destructive-clear defect was itself deleting live <c>user:*</c> keys, and
    /// `NotThrowAsync&lt;WholeDatabaseDeleteException&gt;` swallowed every sign of it. It would also have
    /// deleted other fixtures' keys once STORY-042 stands up a Docker-gated Redis.
    /// </para>
    /// </summary>
    private const string Unroutable = "192.0.2.1:6379,connectTimeout=150,connectRetry=1,abortConnect=true";

    private static RedisCache Unprefixed(int database = 0) =>
        new(new RedisSettings { RawConnectionString = Unroutable, Database = database });

    private static RedisCache Prefixed(string prefix = "test") =>
        new(new RedisSettings { RawConnectionString = Unroutable, KeyPrefix = prefix });

    // ---- the defect itself -------------------------------------------------

    [Fact]
    public async Task ClearAsync_WithNoKeyPrefix_RefusesInsteadOfFlushingTheDatabase()
    {
        using var cache = Unprefixed();

        var act = async () => await cache.ClearAsync();

        (await act.Should().ThrowAsync<WholeDatabaseDeleteException>())
            .Which.Operation.Should().Be("ClearAsync");
    }

    [Fact]
    public async Task RemoveByPrefixAsync_WithNoKeyPrefixAndEmptyPrefix_RefusesTheSecondDoorToo()
    {
        using var cache = Unprefixed();

        var act = async () => await cache.RemoveByPrefixAsync("");

        // This is the door that was live on EVERY configuration pre-fix: SCAN "*" + DEL, neither admin-gated.
        // ClearAsync's FLUSHDB was the loud one; this was the silent one.
        (await act.Should().ThrowAsync<WholeDatabaseDeleteException>())
            .Which.Operation.Should().Be("RemoveByPrefixAsync(\"\")");
    }

    [Fact]
    public async Task RemoveByPrefixAsync_WithNullPrefix_ReportsNullNotEmptyString()
    {
        using var cache = Unprefixed();

        var act = async () => await cache.RemoveByPrefixAsync(null!);

        // Both normalise to the same empty scope, but an operator greps for the call they made.
        (await act.Should().ThrowAsync<WholeDatabaseDeleteException>())
            .Which.Operation.Should().Be("RemoveByPrefixAsync(null)");
    }

    [Fact]
    public async Task ClearAsync_RefusalNamesBothOptOuts()
    {
        using var cache = Unprefixed();

        var act = async () => await cache.ClearAsync();

        var message = (await act.Should().ThrowAsync<WholeDatabaseDeleteException>()).Which.Message;
        // The refusal is only useful if it says how to proceed deliberately — configure a namespace, or
        // name the destructive operation. A guard that just says "no" gets worked around.
        message.Should().Contain("KeyPrefix");
        message.Should().Contain("FlushDatabaseAsync");
    }

    [Fact]
    public async Task ClearAsync_RefusalReportsTheDatabaseItWouldHaveEmptied()
    {
        using var cache = Unprefixed(database: 7);

        var act = async () => await cache.ClearAsync();

        (await act.Should().ThrowAsync<WholeDatabaseDeleteException>())
            .Which.Database.Should().Be(7);
    }

    [Fact]
    public async Task WholeDatabaseDeleteException_IsCatchableAsInvalidOperationException()
    {
        using var cache = Unprefixed();

        var act = async () => await cache.ClearAsync();

        // Mirrors WholeTableWriteException / TenantScopeRequiredException: existing
        // catch (InvalidOperationException) blocks keep working.
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- the refusal must not fire on the cases it was never about ---------

    [Fact]
    public async Task ClearAsync_WithAKeyPrefix_IsNotRefused()
    {
        using var cache = Prefixed();

        var act = async () => await cache.ClearAsync();

        // It will fail to reach localhost:6379 in CI, which is fine — the point is that it gets far enough
        // to try. Widening a guard must not narrow the working path.
        await act.Should().NotThrowAsync<WholeDatabaseDeleteException>();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_WithANonEmptyPrefix_IsNotRefusedEvenWithNoKeyPrefix()
    {
        using var cache = Unprefixed();

        var act = async () => await cache.RemoveByPrefixAsync("user:");

        // A caller-supplied prefix bounds the pattern on its own, so ownership is not in question. Refusing
        // here would have broken a legitimate call in the name of closing the hole.
        await act.Should().NotThrowAsync<WholeDatabaseDeleteException>();
    }

    [Fact]
    public async Task ClearAsync_StillHonorsCancellationBeforeTheScopeCheck()
    {
        using var cache = Unprefixed();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await cache.ClearAsync(cts.Token);

        // CR-M034's contract survives the new guard: a pre-cancelled token wins, so a caller who cancelled
        // does not get told their configuration is wrong instead.
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- the explicit flush door -------------------------------------------

    [Fact]
    public void FlushDatabaseAsync_IsNotReachableThroughICache()
    {
        // The criterion this pins is "FLUSHDB is not reachable from ICache". Asserting it by construction
        // ("I didn't add it to the interface") is not evidence — the next person to widen ICache would break
        // it silently, and nothing would fail. Reflection over the interface is the check that survives them.
        typeof(ICache).GetMethod("FlushDatabaseAsync").Should().BeNull(
            "a cache-shaped contract must not be able to empty a database");

        typeof(RedisCache).GetMethod("FlushDatabaseAsync").Should().NotBeNull(
            "the deliberate caller still needs a door, it just has to name the operation");
    }

    [Fact]
    public async Task FlushDatabaseAsync_HonorsCancellation()
    {
        using var cache = Unprefixed();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await cache.FlushDatabaseAsync(cts.Token);

        // Same CR-M034 contract every other Redis-calling method on this type holds to.
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FlushDatabaseAsync_IsNotItselfRefusedByTheScopeGuard()
    {
        using var cache = Unprefixed();

        var act = async () => await cache.FlushDatabaseAsync();

        // The escape hatch must not be caught by the guard it is the escape from. This asserts only that —
        // NOT that the flush succeeds; see FlushDatabaseAsync_RequiresAdminMode for why it cannot on a
        // settings-built connection, and why an assertion of the broader property would be untestable here.
        await act.Should().NotThrowAsync<WholeDatabaseDeleteException>();
    }

    [Fact]
    public void FlushDatabaseAsync_RequiresAdminMode_WhichSettingsCannotProduce()
    {
        // The close-gate review's headline finding, verified by reflecting the shipped StackExchange.Redis
        // 2.8.41: FLUSHDB and KEYS carry Message.IsAdmin == true; SCAN and DEL do not. So the flush door is
        // gated behind allowAdmin=true, and GetConnectionString() never emits it.
        //
        // This is why WholeDatabaseDeleteException's message names KeyPrefix FIRST and states the admin
        // precondition on the flush door: without it the guard would send an operator to a door that answers
        // with a second, unrelated exception — the "fail-fast is only legitimate where an opt-out exists"
        // rule failing on its own terms. The test pins the precondition so a future settings change that
        // starts emitting allowAdmin (or stops) is caught here rather than in a message that quietly lies.
        var options = ConfigurationOptions.Parse(new RedisSettings("localhost").GetConnectionString());

        options.AllowAdmin.Should().BeFalse(
            "GetConnectionString() must not silently unlock FLUSHDB/CONFIG/SHUTDOWN for every Redis consumer");

        var message = new WholeDatabaseDeleteException("ClearAsync", 0).Message;
        message.Should().Contain("allowAdmin=true");
        message.Should().Contain("RawConnectionString");
        message.IndexOf("KeyPrefix", StringComparison.Ordinal).Should()
            .BeLessThan(message.IndexOf("FlushDatabaseAsync", StringComparison.Ordinal),
                "the opt-out that works on every configuration must be the one named first");
    }

    // ---- the decision table, pinned directly ------------------------------

    [Theory]
    // no KeyPrefix + no caller prefix => "*" => the whole database => refuse
    [InlineData(null, "", null)]
    // a configured KeyPrefix always contributes at least "{prefix}:", so it is always bounded
    [InlineData("app", "", "app:*")]
    [InlineData("app", "user:", "app:user:*")]
    // no KeyPrefix but a caller prefix is still bounded — this is the case a blunter guard would have broken
    [InlineData(null, "user:", "user:*")]
    public void ResolveOwnedKeyPattern_ReturnsNullOnlyWhenTheScopeIsTheWholeDatabase(
        string? keyPrefix, string prefix, string? expected)
    {
        RedisCache.ResolveOwnedKeyPattern(keyPrefix, prefix).Should().Be(expected);
    }

    [Fact]
    public void ResolveOwnedKeyPattern_TreatsAnEmptyKeyPrefixAsANamespace()
    {
        // "" is not null: the caller set a prefix, so keys are written as ":{key}" and ":*" scans exactly
        // those. Odd, but bounded — and RedisSettings distinguishes the two, so this must not collapse them.
        RedisCache.ResolveOwnedKeyPattern("", "").Should().Be(":*");
    }

    // ---- glob metacharacters must not turn a prefix back into "everything" --

    [Fact]
    public void ResolveOwnedKeyPattern_EscapesAStarPrefixThatWouldOtherwiseMatchEverything()
    {
        // Found by the close-gate security pass. Unescaped, this resolved to "**" — non-null, so it passed
        // the emptiness guard, and "**" matches EVERY key in the database. The guard against a whole-database
        // delete was one character wide.
        RedisCache.ResolveOwnedKeyPattern(null, "*").Should().Be("\\**");
    }

    [Fact]
    public void ResolveOwnedKeyPattern_EscapesAStarKeyPrefixSoClearStaysInItsNamespace()
    {
        // The ClearAsync half of the same hole: "*:*" matched every colon-containing key, i.e. every
        // sibling component's namespaced keys.
        RedisCache.ResolveOwnedKeyPattern("*", "").Should().Be("\\*:*");
    }

    [Theory]
    [InlineData("a*b", "a\\*b")]
    [InlineData("a?b", "a\\?b")]
    [InlineData("a[b]c", "a\\[b\\]c")]
    [InlineData(@"a\b", @"a\\b")]
    [InlineData("plain:key", "plain:key")]
    public void EscapeGlob_EscapesEveryRedisMetacharacterAndNothingElse(string input, string expected)
    {
        RedisCache.EscapeGlob(input).Should().Be(expected);
    }

    [Fact]
    public void EscapeGlob_EscapesTheBackslashFirst()
    {
        // Order matters: escaping "\" after "*" would turn "\*" into "\\\*" — the backslash of the first
        // escape getting escaped by the second pass, so the pattern no longer means what it says.
        RedisCache.EscapeGlob(@"\*").Should().Be(@"\\\*");
    }

    [Fact]
    public async Task RemoveByPrefixAsync_WithAGlobPrefix_ScansForALiteralNotAWildcard()
    {
        using var cache = Unprefixed();

        var act = async () => await cache.RemoveByPrefixAsync("*");

        // It is not refused — a key literally starting with "*" is a legitimate thing to remove — but the
        // pattern it builds is bounded. The refusal and the escaping are two different mechanisms and this
        // asserts the right one applies here.
        await act.Should().NotThrowAsync<WholeDatabaseDeleteException>();
        RedisCache.ResolveOwnedKeyPattern(null, "*").Should().NotBe("**");
    }
}
