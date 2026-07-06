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
}
