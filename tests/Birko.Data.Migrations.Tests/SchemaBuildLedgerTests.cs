using System.Collections.Generic;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.Patterns.Schema;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.Tests;

/// <summary>
/// TASK-627: the ledger a schema builder keeps of the chains it handed out. The tracked builder forwards every call to
/// the provider's own and stays the chain's receiver, so a chain is finished exactly when its <c>Build()</c> runs.
/// </summary>
public class SchemaBuildLedgerTests
{
    private sealed class RecordingIndexBuilder : IIndexBuilder
    {
        public List<string> Calls { get; } = new();
        public IIndexBuilder WithField(string name, bool descending = false, IndexFieldType fieldType = IndexFieldType.Standard) { Calls.Add("field:" + name + (descending ? ":desc" : "")); return this; }
        public IIndexBuilder Unique() { Calls.Add("unique"); return this; }
        public IIndexBuilder Sparse() { Calls.Add("sparse"); return this; }
        public IIndexBuilder WithProperty(string key, object value) { Calls.Add("prop:" + key); return this; }
        public void Build() => Calls.Add("build");
    }

    private sealed class RecordingCollectionBuilder : ICollectionBuilder
    {
        public List<string> Calls { get; } = new();
        public ICollectionBuilder WithField(string name, FieldType type, bool isPrimary = false, bool isUnique = false,
            bool isRequired = false, int? maxLength = null, int? precision = null, int? scale = null,
            bool isAutoIncrement = false, object? defaultValue = null) { Calls.Add("field:" + name + (isPrimary ? ":pk" : "")); return this; }
        public ICollectionBuilder WithField(FieldDescriptor field) { Calls.Add("descriptor:" + field.Name); return this; }
        public void Build() => Calls.Add("build");
    }

    [Fact]
    public void A_chain_is_pending_until_built_and_every_call_reaches_the_provider()
    {
        var ledger = new SchemaBuildLedger();
        var inner = new RecordingIndexBuilder();

        var chain = ledger.Track(inner, "Orders", "IX_Number").WithField("Number", descending: true).Unique().Sparse().WithProperty("k", 1);

        ledger.Pending.Should().Equal("CreateIndex(\"Orders\", \"IX_Number\")");
        chain.Build();
        ledger.Pending.Should().BeEmpty();
        inner.Calls.Should().Equal("field:Number:desc", "unique", "sparse", "prop:k", "build");
    }

    [Fact]
    public void Collections_are_tracked_the_same_way()
    {
        var ledger = new SchemaBuildLedger();
        var inner = new RecordingCollectionBuilder();

        var chain = ledger.Track(inner, "Orders").WithField("Id", FieldType.Guid, isPrimary: true);
        ledger.Track(new RecordingCollectionBuilder(), "Lines");
        chain.Build();

        ledger.Pending.Should().Equal("CreateCollection(\"Lines\")");
        inner.Calls.Should().Equal("field:Id:pk", "build");
    }

    [Fact]
    public void A_builder_that_does_not_track_reports_nothing_pending()
    {
        ISchemaBuilder untracked = new UntrackedSchemaBuilder();
        untracked.PendingBuilds.Should().BeEmpty();
    }

    private sealed class UntrackedSchemaBuilder : ISchemaBuilder
    {
        public ICollectionBuilder CreateCollection(string name) => new RecordingCollectionBuilder();
        public void DropCollection(string name) { }
        public bool CollectionExists(string name) => false;
        public IIndexBuilder CreateIndex(string collectionName, string indexName) => new RecordingIndexBuilder();
        public void DropIndex(string collectionName, string indexName) { }
        public void AddField(string collectionName, FieldDescriptor field) { }
        public void DropField(string collectionName, string fieldName) { }
        public void RenameField(string collectionName, string oldName, string newName) { }
    }
}
