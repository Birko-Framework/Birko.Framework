using System;
using Birko.Data.Patterns.Schema;
using FluentAssertions;
using Xunit;
using Birko.Data.Migrations.ElasticSearch.Context;
using Nest;

namespace Birko.Data.Migrations.ElasticSearch.Tests;

/// <summary>
/// TASK-274 — this backend's migration index builder refuses what it cannot honour instead of accepting it
/// and doing nothing.
/// </summary>
/// <remarks>
/// <para>
/// ElasticSearch has no secondary-index concept — its inverted index comes from the mapping — so a field-list index declaration can never be honoured here.
/// </para>
/// <para>
/// Every method on <c>IIndexBuilder</c> returns <c>this</c>, so a builder that cannot express something has a
/// silent option at every step — and this one took it. Refusing was affordable because nothing called it:
/// measured 0 uses of <c>.Sparse()</c> and <c>.WithProperty(</c> across the framework, its tests and all 16
/// consumer repos. No server is needed: the refusals happen before any request.
/// </para>
/// </remarks>
public class IndexBuilderRefusalTests
{
    private static IIndexBuilder Builder()
        => new ElasticSearchSchemaBuilder(new ElasticClient(new ConnectionSettings(new Uri("http://localhost:9200"))))
            .CreateIndex("Rows", "ix_probe");

    [Fact]
    public void Sparse_is_refused()
    {
        Action act = () => Builder().WithField("Code").Sparse();

        act.Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("sparse index");
    }

    [Fact]
    public void WithProperty_is_refused()
    {
        Action act = () => Builder().WithField("Code").WithProperty("k", 1);

        act.Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("index property 'k'");
    }

    /// <summary>
    /// The whole declaration is refused. This builder used to inherit <c>IIndexBuilder.Build()</c>'s no-op
    /// default while accumulating fields and a <c>Unique()</c> flag and holding a live client — so a migration
    /// read as though it had declared an index and the database never got one. TASK-246's lost-flag defect,
    /// total rather than partial.
    /// </summary>
    [Fact]
    public void Build_refuses_rather_than_creating_nothing()
    {
        Action act = () => Builder().WithField("Code").Unique().Build();

        act.Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("does not create indexes");
    }
}
