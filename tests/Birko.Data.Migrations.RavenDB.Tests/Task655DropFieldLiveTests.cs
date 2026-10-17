using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Migrations.RavenDB.Context;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.RavenDB.Tests;

/// <summary>
/// TASK-655, live RavenDB: <c>DropField</c> removes the field from every document of the collection, and only of that
/// collection. It was a no-op, and the migration that called it was recorded. Gated on <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public sealed class Task655DropFieldLiveTests : IDisposable
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    private readonly IDocumentStore? _store;
    private readonly string _db = "birko-task655-" + Guid.NewGuid().ToString("N");

    public Task655DropFieldLiveTests(ITestOutputHelper output)
    {
        _output = output;
        if (string.IsNullOrWhiteSpace(Url))
        {
            const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return;
        }
        _store = new DocumentStore { Urls = new[] { Url! }, Database = _db }.Initialize();
        _store.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(_db)));
    }

    public void Dispose()
    {
        if (_store == null) return;
        try { _store.Maintenance.Server.Send(new DeleteDatabasesOperation(_db, hardDelete: true)); } catch { }
        _store.Dispose();
    }

    private void Seed(string collection, params string[] names)
        => new RavenDBDataMigrator(_store!).BulkInsert(collection, names.Select(n => (IDictionary<string, object>)new Dictionary<string, object>
        {
            ["Name"] = n,
            ["Secret"] = "s-" + n,
        }));

    private List<Dictionary<string, object?>> All(string collection)
    {
        using var session = _store!.OpenSession();
        return session.Advanced.RawQuery<Dictionary<string, object?>>($"from '{collection}'").WaitForNonStaleResults().ToList();
    }

    [Fact]
    public void DropField_removes_the_field_from_the_collection_only()
    {
        if (_store == null) return;
        Seed("Accounts", "a", "b");
        Seed("Others", "c");

        new RavenDBSchemaBuilder(_store).DropField("Accounts", "Secret");

        var accounts = All("Accounts");
        accounts.Should().HaveCount(2).And.OnlyContain(d => !d.ContainsKey("Secret"));
        accounts.Select(d => (string?)d["Name"]).Should().BeEquivalentTo(new[] { "a", "b" });
        All("Others").Should().ContainSingle().Which.Should().ContainKey("Secret", "another collection is untouched");
    }

    [Fact]
    public void A_field_name_that_is_not_a_plain_path_is_refused()
    {
        if (_store == null) return;

        FluentActions.Invoking(() => new RavenDBSchemaBuilder(_store).DropField("Accounts", "Secret; delete this.Name"))
            .Should().Throw<NotSupportedException>("TASK-640's identifier rule");
    }
}
