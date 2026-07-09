using Birko.Data.Migrations.RavenDB.Context;
using FluentAssertions;
using Raven.Client.Documents;
using System;
using Xunit;

namespace Birko.Data.Migrations.RavenDB.Tests;

/// <summary>
/// Tests for the RavenDB data migrator. The mere existence of this project compile-verifies the
/// LINQ usings that were missing (CR-C10 in RavenDBDataMigrator, CR-C11 in RavenMigrationRunner);
/// without them the shared project fails to build here.
///
/// CountDocuments (CR-C12) and the LINQ-heavy query paths need a live RavenDB server and are not
/// exercised here — that is the documented infra gap for this backend.
/// </summary>
public class RavenDBDataMigratorTests
{
    // CopyData previously ignored targetCollection/transformJson and silently re-saved documents
    // onto the source collection (CR-C13). It now fails fast until a verified cross-collection copy
    // lands. The throw happens before any session is opened, so no server is required.
    [Fact]
    public void CopyData_IsNotSupported_AndThrowsBeforeTouchingTheServer()
    {
        using var store = new DocumentStore { Urls = new[] { "http://localhost:59999" }, Database = "unused" };
        var migrator = new RavenDBDataMigrator(store);

        Action act = () => migrator.CopyData("SourceCollection", "TargetCollection", null);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*CopyData*");
    }

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        Action act = () => new RavenDBDataMigrator(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // CR-H066 / CR-M114: cover the pure ParseFilterToRql helper (no live server needed). Values are
    // now bound $pN query parameters instead of interpolated literals.
    [Fact]
    public void ParseFilterToRql_Empty_ReturnsEmpty()
    {
        RavenDBDataMigrator.ParseFilterToRql(null).Rql.Should().BeEmpty();
        RavenDBDataMigrator.ParseFilterToRql(null).Parameters.Should().BeEmpty();
        RavenDBDataMigrator.ParseFilterToRql("{}").Rql.Should().BeEmpty();
    }

    [Fact]
    public void ParseFilterToRql_Equality_uses_a_bound_parameter()
    {
        var (rql, parameters) = RavenDBDataMigrator.ParseFilterToRql("{\"status\":\"active\"}");

        rql.Should().Be("status = $p0");
        parameters["p0"].Should().Be("active");
    }

    [Theory]
    [InlineData("$gt", ">")]
    [InlineData("$gte", ">=")]
    [InlineData("$lt", "<")]
    [InlineData("$lte", "<=")]
    [InlineData("$ne", "!=")]
    public void ParseFilterToRql_operators_map_with_bound_values(string mongoOp, string rqlOp)
    {
        var (rql, parameters) = RavenDBDataMigrator.ParseFilterToRql($"{{\"age\":{{\"{mongoOp}\":18}}}}");

        rql.Should().Be($"age {rqlOp} $p0");
        parameters["p0"].Should().Be(18L);
    }

    [Fact]
    public void ParseFilterToRql_MultipleConditions_JoinedWithAnd()
    {
        var (rql, parameters) = RavenDBDataMigrator.ParseFilterToRql("{\"a\":1,\"b\":\"x\"}");

        rql.Should().Be("a = $p0 AND b = $p1");
        parameters["p0"].Should().Be(1L);
        parameters["p1"].Should().Be("x");
    }

    [Fact]
    public void ParseFilterToRql_value_with_a_quote_stays_a_bound_parameter()
    {
        // CR-M114: a value containing a single quote no longer breaks the RQL literal — it is a param.
        var (rql, parameters) = RavenDBDataMigrator.ParseFilterToRql("{\"name\":\"O'Brien\"}");

        rql.Should().Be("name = $p0");
        parameters["p0"].Should().Be("O'Brien");
    }
}
