using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Migrations.RavenDB.Context;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.RavenDB.Tests;

/// <summary>
/// TASK-640, live RavenDB: a collection name, an update key and <c>RenameField</c>'s field names reach RQL text without
/// changing what the query means. Gated on <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public sealed class Task640RqlNameLiveTests : IDisposable
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    private readonly IDocumentStore? _store;
    private readonly string _db = "birko-task640-" + Guid.NewGuid().ToString("N");

    public Task640RqlNameLiveTests(ITestOutputHelper output)
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

    private void Seed(string collection, params (string Name, string Role)[] docs)
        => new RavenDBDataMigrator(_store!).BulkInsert(collection, docs.Select(d => (IDictionary<string, object>)new Dictionary<string, object>
        {
            ["Name"] = d.Name,
            ["Role"] = d.Role,
        }));

    private List<Dictionary<string, object?>> All(string collection)
    {
        using var session = _store!.OpenSession();
        return session.Advanced.RawQuery<Dictionary<string, object?>>($"from '{collection}'")
            .WaitForNonStaleResults().ToList();
    }

    private const string Injection = "Accounts' WHERE Name = 'b";

    [Fact]
    public void A_collection_named_with_a_quote_and_a_backslash_is_addressed_exactly()
    {
        if (_store == null) return;
        const string odd = "O'Brien\\Co";
        Seed(odd, ("a", "user"), ("b", "user"));
        Seed("Accounts", ("a", "user"));
        var data = new RavenDBDataMigrator(_store);
        var schema = new RavenDBSchemaBuilder(_store);

        data.UpdateDocuments(odd, "{\"Name\":\"a\"}", new Dictionary<string, object> { ["Role"] = "admin" });
        data.CountDocuments(odd, "{\"Role\":\"admin\"}").Should().Be(1);
        schema.RenameField(odd, "Name", "Label");
        data.DeleteDocuments(odd, "{\"Label\":\"b\"}");

        var left = All("O\\'Brien\\\\Co").Single();
        left["Role"].Should().Be("admin");
        left["Label"].Should().Be("a");
        schema.DropCollection(odd);
        All("O\\'Brien\\\\Co").Should().BeEmpty();
        All("Accounts").Should().ContainSingle().Which["Role"].Should().Be("user", "another collection is never touched");
    }

    [Theory]
    [InlineData("Accounts", "'Accounts'")]
    [InlineData("O'Brien", "'O\\'Brien'")]
    [InlineData("a\\b", "'a\\\\b'")]
    [InlineData("Accounts' WHERE Name = 'b", "'Accounts\\' WHERE Name = \\'b'")]
    public void A_collection_becomes_one_escaped_literal(string collection, string rql)
        => RavenDBDataMigrator.RqlCollection(collection).Should().Be(rql);

    [Theory]
    [InlineData("Name", true)]
    [InlineData("Address.City", true)]
    [InlineData("Role = 'admin'; this.Note", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    public void A_field_is_a_plain_identifier_path_or_refused(string field, bool accepted)
    {
        var act = () => RavenDBDataMigrator.RqlField(field);
        if (accepted) act.Should().NotThrow(); else act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void An_update_key_cannot_write_a_second_field()
    {
        if (_store == null) return;
        Seed("Accounts", ("a", "user"));
        var data = new RavenDBDataMigrator(_store);

        var act = () => data.UpdateDocuments("Accounts", "{\"Name\":\"a\"}", new Dictionary<string, object> { ["Role = 'admin'; this.Note"] = "x" });

        act.Should().Throw<NotSupportedException>();
        All("Accounts").Single()["Role"].Should().Be("user");
    }

    [Fact]
    public void A_collection_name_cannot_narrow_a_delete_or_a_drop()
    {
        if (_store == null) return;
        Seed("Accounts", ("a", "user"), ("b", "user"), ("c", "user"));

        var drop = () => new RavenDBSchemaBuilder(_store).DropCollection(Injection);
        var delete = () => new RavenDBDataMigrator(_store).DeleteDocuments(Injection, "{\"Role\":\"user\"}");

        try { drop(); } catch (Exception ex) { _output.WriteLine("drop: " + ex.GetType().Name); }
        try { delete(); } catch (Exception ex) { _output.WriteLine("delete: " + ex.GetType().Name); }
        All("Accounts").Select(d => d["Name"]).Should().BeEquivalentTo(new[] { "a", "b", "c" }, "no collection is called that");
    }

    [Fact]
    public void A_collection_name_cannot_narrow_an_update()
    {
        if (_store == null) return;
        Seed("Accounts", ("a", "user"), ("b", "user"));

        try { new RavenDBDataMigrator(_store).UpdateDocuments(Injection, "{\"Role\":\"user\"}", new Dictionary<string, object> { ["Role"] = "admin" }); }
        catch (Exception ex) { _output.WriteLine("update: " + ex.GetType().Name); }

        All("Accounts").Select(d => d["Role"]).Should().AllBeEquivalentTo("user");
    }

    [Fact]
    public void RenameField_names_cannot_write_another_field_or_narrow_the_collection()
    {
        if (_store == null) return;
        Seed("Accounts", ("a", "user"), ("b", "user"));
        var schema = new RavenDBSchemaBuilder(_store);

        var field = () => schema.RenameField("Accounts", "Name", "Role = 'admin'; this.Label");
        var collection = () => schema.RenameField(Injection, "Name", "Label");

        field.Should().Throw<NotSupportedException>();
        try { collection(); } catch (Exception ex) { _output.WriteLine("rename collection: " + ex.GetType().Name); }
        All("Accounts").Should().OnlyContain(d => (string?)d["Role"] == "user" && d.ContainsKey("Name") && !d.ContainsKey("Label"));
    }

    [Fact]
    public void A_dotted_update_key_and_RenameField_still_work()
    {
        if (_store == null) return;
        Seed("Accounts", ("a", "user"));
        new RavenDBDataMigrator(_store).UpdateDocuments("Accounts", "{\"Name\":\"a\"}", new Dictionary<string, object> { ["Role"] = "admin" });
        new RavenDBSchemaBuilder(_store).RenameField("Accounts", "Name", "Label");

        var doc = All("Accounts").Single();
        doc["Role"].Should().Be("admin");
        doc["Label"].Should().Be("a");
        doc.Should().NotContainKey("Name");
    }
}
