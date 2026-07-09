using System;
using Birko.Data.MongoDB.IndexManagement;
using Birko.Data.MongoDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// CR-M120: pure-logic coverage that needs no live MongoDB — Settings.GetConnectionString assembly
/// (credentials / authSource / replicaSet / tls / retry) and GetId, plus the index manager's
/// ValidateScope guard.
/// </summary>
public class MongoSettingsAndIndexTests
{
    [Fact]
    public void GetConnectionString_minimal_has_authsource_and_retry_defaults()
    {
        var settings = new Settings { Location = "localhost", Port = 27017 };

        settings.GetConnectionString()
            .Should().Be("mongodb://localhost:27017?authSource=admin&retryWrites=true&retryReads=true");
    }

    [Fact]
    public void GetConnectionString_includes_credentials_and_database()
    {
        var settings = new Settings
        {
            Location = "db.example.com",
            Port = 27017,
            Name = "shop",
            UserName = "svc",
            Password = "secret",
        };

        settings.GetConnectionString()
            .Should().Be("mongodb://svc:secret@db.example.com:27017/shop?authSource=admin&retryWrites=true&retryReads=true");
    }

    [Fact]
    public void GetConnectionString_includes_replicaset_and_tls_when_set()
    {
        var settings = new Settings
        {
            Location = "localhost",
            Port = 27017,
            ReplicaSet = "rs0",
            UseSecure = true,
        };

        var cs = settings.GetConnectionString();

        cs.Should().Contain("replicaSet=rs0");
        cs.Should().Contain("tls=true");
    }

    [Fact]
    public void GetConnectionString_omits_credentials_when_only_username_set()
    {
        var settings = new Settings { Location = "localhost", Port = 27017, UserName = "svc" };

        settings.GetConnectionString().Should().NotContain("@");
    }

    [Fact]
    public void GetId_composes_location_port_name_username()
    {
        var settings = new Settings { Location = "loc", Port = 27017, Name = "db", UserName = "u" };

        settings.GetId().Should().Be("loc:27017:db:u");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateScope_rejects_missing_scope(string? scope)
    {
        Action act = () => MongoDBIndexManager.ValidateScope(scope);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateScope_accepts_a_collection_name()
    {
        Action act = () => MongoDBIndexManager.ValidateScope("products");

        act.Should().NotThrow();
    }
}
