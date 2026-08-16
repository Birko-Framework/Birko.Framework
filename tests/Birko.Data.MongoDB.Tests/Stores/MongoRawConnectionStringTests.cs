using System;
using Birko.Data.MongoDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-225 — <c>GetConnectionString()</c> composed the URI and appended a <b>fixed</b> set of query
/// parameters (<c>authSource</c>, <c>replicaSet</c>, <c>tls</c>, <c>retryWrites</c>, <c>retryReads</c>),
/// with no way to add any other. Everything a real deployment eventually needs was unreachable:
/// <c>maxPoolSize</c>, <c>appName</c>, <c>connectTimeoutMS</c>, <c>readPreference</c>, write concern,
/// <c>directConnection</c>, and the SOCKS <c>proxyHost</c>/<c>proxyPort</c> pair — MongoDB's nearest
/// equivalent to the CosmosDB Gateway mode added in TASK-223.
///
/// <para>
/// Not hypothetical: the framework's own live probes had to subclass <c>Settings</c> and override this
/// method three times in one session (TASK-214, TASK-219) merely to set a server-selection timeout.
/// </para>
///
/// <para>
/// Shaped deliberately like <c>Birko.Redis.RedisSettings.RawConnectionString</c> so the family has one
/// answer rather than two — including that only a NON-EMPTY value overrides.
/// </para>
/// </summary>
public class MongoRawConnectionStringTests
{
    private const string Raw =
        "mongodb://user:pw@h1:27017,h2:27017/db?replicaSet=rs0&maxPoolSize=200&appName=svc&proxyHost=127.0.0.1";

    [Fact]
    public void A_raw_string_is_returned_verbatim()
    {
        // Verbatim matters: the whole point is that the caller owns the string. Re-appending anything —
        // authSource, retryWrites — could contradict what they wrote.
        new Settings("ignored", "ignored") { RawConnectionString = Raw }
            .GetConnectionString().Should().Be(Raw);
    }

    [Fact]
    public void The_composed_form_is_unchanged_when_the_hatch_is_unset()
    {
        // The contract for every existing consumer: adding the property must change nothing for them.
        var composed = new Settings("localhost", "mydb").GetConnectionString();

        composed.Should().StartWith("mongodb://localhost:27017/mydb?");
        composed.Should().Contain("authSource=admin")
            .And.Contain("retryWrites=true")
            .And.Contain("retryReads=true");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_unset_or_empty_hatch_falls_through_to_the_composed_form(string? raw)
    {
        // The empty case is the one that bites. Returning "" verbatim would hand the driver an invalid
        // connection string — exactly the correction RedisSettings needed in CR-L331, mirrored here
        // rather than rediscovered.
        var settings = new Settings("localhost", "mydb") { RawConnectionString = raw };

        settings.GetConnectionString().Should().Be(new Settings("localhost", "mydb").GetConnectionString());
    }

    [Fact]
    public void LoadFrom_carries_the_raw_string()
    {
        // LoadFrom copies the sibling settings; omitting this one would silently drop a consumer's whole
        // connection configuration wherever settings are cloned or loaded — and fall back to a composed
        // string that happens to be valid, so it would connect to the wrong place without an error.
        var source = new Settings("h", "db") { RawConnectionString = Raw };

        var target = new Settings();
        target.LoadFrom(source);

        target.RawConnectionString.Should().Be(Raw);
        target.GetConnectionString().Should().Be(Raw);
    }

    [Fact]
    public void A_store_built_from_raw_settings_uses_it()
    {
        // End to end through the store's own path, no server: MongoClient construction parses the
        // connection string, so an unusable one throws here.
        var store = new AsyncMongoDBStore<Doc>();

        var act = () => store.SetSettings(new Settings("ignored", "db") { RawConnectionString = Raw });

        act.Should().NotThrow();
    }

    public class Doc : Birko.Data.Models.AbstractModel { public string? Name { get; set; } }
}
