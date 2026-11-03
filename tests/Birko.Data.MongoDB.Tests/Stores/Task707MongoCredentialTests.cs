using Birko.Data.MongoDB.Stores;
using FluentAssertions;
using MongoDB.Driver;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-707. <c>GetConnectionString()</c> wrote the user name, password, database, auth database and replica set into
/// the URI as written. Measured with the driver's own parser: the password <c>p@ss/w?rd</c> made the server
/// <c>ss:27017</c> and the database <c>w</c>; a <c>:</c> in the user name made the URI invalid; an auth database of
/// <c>admin&amp;directConnection=true</c> turned on <c>directConnection</c>; a <c>%41</c> in a password reached the
/// server as <c>A</c>. Each value is now percent-encoded, as the MongoDB URI format requires.
/// </summary>
public class Task707MongoCredentialTests
{
    private static MongoUrl Parse(Settings s) => MongoUrl.Create(s.GetConnectionString());

    [Theory]
    [InlineData("svc", "p@ss/w?rd")]
    [InlineData("svc", "pw@evil:27017/x")]
    [InlineData("s:vc", "pw")]
    [InlineData("svc", "pw%41")]
    [InlineData("s v@c", "p&w=#:")]
    public void Credentials_reach_the_driver_whole(string user, string password)
    {
        var url = Parse(new Settings { Location = "db", Port = 27017, Name = "shop", UserName = user, Password = password });

        url.Username.Should().Be(user);
        url.Password.Should().Be(password);
        url.Server.Should().Be(new MongoServerAddress("db", 27017));
        url.DatabaseName.Should().Be("shop");
    }

    [Fact]
    public void An_auth_database_cannot_add_an_option()
    {
        var url = Parse(new Settings { Location = "db", Port = 27017, Name = "shop", AuthDatabase = "admin&directConnection=true" });

        url.AuthenticationSource.Should().Be("admin&directConnection=true");
        url.DirectConnection.Should().BeFalse();
    }

    [Fact]
    public void A_replica_set_cannot_add_an_option()
    {
        var url = Parse(new Settings { Location = "db", Port = 27017, Name = "shop", ReplicaSet = "rs0&tls=false", UseSecure = true });

        url.ReplicaSetName.Should().Be("rs0&tls=false");
        url.UseTls.Should().BeTrue();
    }

    [Fact]
    public void A_database_name_cannot_add_an_option()
    {
        var url = Parse(new Settings { Location = "db", Port = 27017, Name = "shop?directConnection=true" });

        url.DatabaseName.Should().Be("shop?directConnection=true");
        url.DirectConnection.Should().BeFalse();
    }
}
