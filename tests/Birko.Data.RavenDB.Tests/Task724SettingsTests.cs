using System;
using Birko.Data.RavenDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.RavenDB.Tests;

/// <summary>
/// TASK-724. Measured before the fix: <c>Settings.CreateDocumentStore</c> accepted a user name and password and never used
/// them — RavenDB authenticates with a client certificate — so a caller believing the store signed in got an anonymous
/// one. A credential it cannot use is refused.
/// </summary>
public class Task724SettingsTests
{
    [Theory]
    [InlineData("admin", null)]
    [InlineData(null, "secret")]
    public void Credentials_RavenDB_cannot_use_are_refused(string? user, string? password)
    {
        var settings = new Settings("http://localhost:8080", "db", user, password);

        FluentActions.Invoking(() => settings.CreateDocumentStore()).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void No_credentials_still_make_a_store()
    {
        using var store = new Settings("http://localhost:8080", "db").CreateDocumentStore();

        store.Database.Should().Be("db");
    }
}
