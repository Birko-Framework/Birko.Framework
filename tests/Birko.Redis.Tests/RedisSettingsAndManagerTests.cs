using System;
using Birko.Configuration;
using Birko.Redis;
using FluentAssertions;
using Xunit;

namespace Birko.Redis.Tests;

/// <summary>
/// CR-M232: Birko.Redis had no test project. Covers RedisSettings.GetConnectionString branch matrix,
/// GetId composition, LoadFrom dispatch, and RedisConnectionManager null-guards / Dispose idempotency /
/// IsConnected-when-not-created (no live Redis — the connection is lazy).
/// </summary>
public class RedisSettingsAndManagerTests
{
    [Fact]
    public void GetConnectionString_DefaultHostPort()
    {
        new RedisSettings().GetConnectionString().Should().Be("localhost:6379");
    }

    [Fact]
    public void GetConnectionString_RawOverride_WinsVerbatim()
    {
        var s = new RedisSettings { RawConnectionString = "r1,r2,abortConnect=false" };
        s.GetConnectionString().Should().Be("r1,r2,abortConnect=false");
    }

    [Fact]
    public void GetConnectionString_ComposesAllFacets()
    {
        var s = new RedisSettings("cache.host", 6380, password: "pw", database: 3, useSsl: true)
        {
            UserName = "acl-user",
            Name = "svc",
        };

        var cs = s.GetConnectionString();
        cs.Should().StartWith("cache.host:6380");
        cs.Should().Contain(",password=pw");
        cs.Should().Contain(",user=acl-user");
        cs.Should().Contain(",ssl=True,sslHost=cache.host");
        cs.Should().Contain(",defaultDatabase=3");
        cs.Should().Contain(",name=svc");
    }

    [Fact]
    public void GetConnectionString_Database0_OmitsDefaultDatabase()
    {
        new RedisSettings("h", 6379, database: 0).GetConnectionString().Should().NotContain("defaultDatabase");
    }

    [Fact]
    public void GetId_IncludesDatabase()
    {
        new RedisSettings("h", 6379, database: 5).GetId().Should().EndWith(":5");
    }

    [Fact]
    public void LoadFrom_Typed_CopiesRedisFields()
    {
        var source = new RedisSettings("h", 6379, database: 7) { KeyPrefix = "app:", RawConnectionString = "raw" };
        var target = new RedisSettings();
        target.LoadFrom(source);

        target.Database.Should().Be(7);
        target.KeyPrefix.Should().Be("app:");
        target.RawConnectionString.Should().Be("raw");
    }

    [Fact]
    public void LoadFrom_Settings_NonRedis_IsNoOp()
    {
        var target = new RedisSettings("h", 6379, database: 2);
        target.LoadFrom(new Settings()); // not a RedisSettings
        target.Database.Should().Be(2, "a foreign Settings must not mutate Redis fields");
    }

    [Fact]
    public void Manager_NullSettings_Throws()
    {
        ((Action)(() => new RedisConnectionManager((RedisSettings)null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new RedisConnectionManager((string)null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Manager_IsConnected_False_WhenNotCreated()
    {
        using var mgr = new RedisConnectionManager("localhost:6379,abortConnect=false");
        mgr.IsConnected.Should().BeFalse("the lazy connection has not been created yet");
    }

    [Fact]
    public void Manager_Dispose_IsIdempotent()
    {
        var mgr = new RedisConnectionManager("localhost:6379,abortConnect=false");
        var act = () => { mgr.Dispose(); mgr.Dispose(); };
        act.Should().NotThrow();
    }

    [Fact]
    public void GetConnectionString_EmptyRawOverride_FallsThroughToProperties()
    {
        // CR-L331: an explicitly-empty RawConnectionString must NOT be returned verbatim.
        var s = new RedisSettings("myhost", 6380) { RawConnectionString = "" };

        s.GetConnectionString().Should().Be("myhost:6380");
    }

    [Fact]
    public void Constructor_LeavesNameAndUserNameNonNull()
    {
        // CR-L332: the ctor passes string.Empty (not null!) for Name/UserName — the non-null contract holds.
        var s = new RedisSettings("myhost");

        s.Name.Should().NotBeNull();
        s.UserName.Should().NotBeNull();
    }
}
