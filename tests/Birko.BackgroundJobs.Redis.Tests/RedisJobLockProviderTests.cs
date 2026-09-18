using System;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Redis;
using Birko.Redis;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.Redis.Tests;

/// <summary>
/// Covers <see cref="RedisJobLockProvider"/> against the contract TASK-232 settled.
/// </summary>
/// <remarks>
/// <para>
/// Before this file, <b>no test in the family touched a lock provider at all</b> — the suite was green
/// while <c>timeout</c> was being passed straight through as the key's expiry with no renewal, so a lock
/// silently expired mid-work on any run longer than that value. Green meant nothing here.
/// </para>
/// <para>
/// The renewal test is the one that matters and it needs a real server, because the whole behaviour is
/// "does the key still exist after its original lease would have elapsed". Gated on
/// <c>BIRKO_REDIS_HOST</c>; the contract tests above it need no server and are therefore not gated —
/// gating something that can run everywhere is how a suite ends up proving nothing.
/// </para>
/// </remarks>
public class RedisJobLockProviderTests
{
    private const string HostEnv = "BIRKO_REDIS_HOST";

    private static RedisSettings? LiveSettings()
    {
        var host = Environment.GetEnvironmentVariable(HostEnv);
        if (string.IsNullOrWhiteSpace(host)) return null;
        return new RedisSettings(host, 6379, string.Empty, 0, false)
        {
            KeyPrefix = "birko:task232:" + Guid.NewGuid().ToString("N"),
        };
    }

    // ---- contract, no server needed -------------------------------------------------------------

    [Fact]
    public void Redis_declares_itself_lease_based()
    {
        using var p = new RedisJobLockProvider(new RedisSettings("localhost", 6379, string.Empty, 0, false));

        ((IJobLockProvider)p).IsLeaseBased.Should().BeTrue(
            "Redis has no server-side session lock, and hiding that behind a uniform interface is what " +
            "made the original single-timeout design unsafe");
    }

    [Fact]
    public void The_default_lease_is_short_because_it_is_renewed()
    {
        RedisJobLockProvider.DefaultLeaseDuration.Should().BeLessThan(TimeSpan.FromMinutes(2),
            "a long unrenewed lease only makes the expire-while-working failure slower; the lease is short " +
            "precisely because a heartbeat extends it");
        RedisJobLockProvider.DefaultLeaseDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_non_positive_lease_is_refused_before_any_connection_is_opened(int seconds)
    {
        // Deliberately points at a port nothing is listening on: the guard must fire before any I/O, so
        // this passes without a server. If the argument check ever moves below GetDatabase() this test
        // starts failing with a connection error instead, which is the signal we want.
        var p = new RedisJobLockProvider(new RedisSettings("localhost", 6399, string.Empty, 0, false));

        var act = () => p.TryAcquireAsync("x", TimeSpan.Zero, TimeSpan.FromSeconds(seconds));

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("leaseDuration");
    }

    // ---- live behaviour --------------------------------------------------------------------------

    [Fact]
    public async Task A_second_provider_cannot_take_a_held_lock()
    {
        var settings = LiveSettings();
        if (settings == null) return;

        await using var first = new RedisJobLockProvider(settings);
        await using var second = new RedisJobLockProvider(settings);

        (await first.TryAcquireAsync("leader", TimeSpan.Zero)).Should().BeTrue();
        (await second.TryAcquireAsync("leader", TimeSpan.Zero)).Should().BeFalse(
            "losing the race is an expected outcome and must return false, not throw");
    }

    [Fact]
    public async Task Releasing_lets_the_next_caller_in()
    {
        var settings = LiveSettings();
        if (settings == null) return;

        await using var first = new RedisJobLockProvider(settings);
        await using var second = new RedisJobLockProvider(settings);

        await first.TryAcquireAsync("handover", TimeSpan.Zero);
        await first.ReleaseAsync("handover");

        (await second.TryAcquireAsync("handover", TimeSpan.Zero)).Should().BeTrue();
    }

    [Fact]
    public async Task Releasing_a_lock_that_is_not_held_is_not_an_error()
    {
        var settings = LiveSettings();
        if (settings == null) return;

        await using var p = new RedisJobLockProvider(settings);

        var act = () => p.ReleaseAsync("never-held");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_heartbeat_keeps_the_lock_past_its_original_lease()
    {
        var settings = LiveSettings();
        if (settings == null) return;

        // A 2s lease renewed at 1s. Without renewal the key is gone by t=2s and the second provider walks
        // straight in — which is exactly what the pre-TASK-232 code did on any run longer than the lease.
        await using var holder = new RedisJobLockProvider(settings);
        (await holder.TryAcquireAsync("long-work", TimeSpan.Zero, TimeSpan.FromSeconds(2)))
            .Should().BeTrue();

        await Task.Delay(TimeSpan.FromSeconds(5));

        await using var intruder = new RedisJobLockProvider(settings);
        (await intruder.TryAcquireAsync("long-work", TimeSpan.Zero)).Should().BeFalse(
            "the heartbeat must have extended the 2s lease across 5s of work; if this fails the lock " +
            "expired while its holder was still running");
        holder.IsLocked.Should().BeTrue();
    }

    [Fact]
    public async Task Disposing_the_holder_frees_the_lock_without_waiting_for_expiry()
    {
        var settings = LiveSettings();
        if (settings == null) return;

        var holder = new RedisJobLockProvider(settings);
        await holder.TryAcquireAsync("crash", TimeSpan.Zero, TimeSpan.FromMinutes(10));
        await holder.DisposeAsync();   // stands in for the process going away cleanly

        await using var next = new RedisJobLockProvider(settings);
        (await next.TryAcquireAsync("crash", TimeSpan.Zero)).Should().BeTrue(
            "dispose releases explicitly rather than leaving a ten-minute lease behind");
    }

    [Fact]
    public async Task Acquire_waits_up_to_the_acquire_timeout_and_then_gives_up()
    {
        var settings = LiveSettings();
        if (settings == null) return;

        await using var holder = new RedisJobLockProvider(settings);
        await holder.TryAcquireAsync("busy", TimeSpan.Zero, TimeSpan.FromMinutes(1));

        await using var waiter = new RedisJobLockProvider(settings);
        var started = DateTime.UtcNow;
        var got = await waiter.TryAcquireAsync("busy", TimeSpan.FromSeconds(1));
        var waited = DateTime.UtcNow - started;

        got.Should().BeFalse();
        waited.Should().BeGreaterThan(TimeSpan.FromMilliseconds(700),
            "acquireTimeout is a wait now, not the lock's lifetime — SET NX does not block so it polls");
        waited.Should().BeLessThan(TimeSpan.FromSeconds(4), "and it must give up rather than hang");
    }
}
