using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// End-to-end proof that a composite [IndexedField(IsUnique: true)] over (TenantGuid, Number) is emitted as a
/// real CREATE UNIQUE INDEX by the SqLite connector and enforced by the engine: a duplicate (TenantGuid, Number)
/// is rejected, while the same Number under a different tenant is allowed (per-tenant, not global, uniqueness).
/// </summary>
public class CompositeUniqueIndexEndToEndTests : IDisposable
{
    private readonly string _root;

    public CompositeUniqueIndexEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-uxidx-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("UxDocs")]
    public class UxDoc : AbstractLogModel
    {
        [IndexedField("ux_docnum", 0, IsUnique: true)]
        public Guid TenantGuid { get; set; }

        [IndexedField("ux_docnum", 1, IsUnique: true)]
        public string Number { get; set; } = null!;
    }

    private AsyncSQLiteStore<UxDoc> NewStore()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "ux.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(UxDoc) }); // emits CREATE UNIQUE INDEX ux_docnum ON UxDocs (TenantGuid, Number)

        var store = new AsyncSQLiteStore<UxDoc>();
        store.SetSettings(new SqLiteSettings(_root, "ux.db"));
        return store;
    }

    [Fact]
    public async Task CompositeUnique_RejectsDuplicatePerTenant_AllowsSameNumberAcrossTenants()
    {
        var store = NewStore();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await store.CreateAsync(new UxDoc { TenantGuid = tenantA, Number = "FV2026000001" });

        // Same tenant + same number → unique-constraint violation.
        await store.Invoking(s => s.CreateAsync(new UxDoc { TenantGuid = tenantA, Number = "FV2026000001" }))
                   .Should().ThrowAsync<Exception>();

        // Different tenant, same number → allowed (uniqueness is per-tenant, not global).
        await store.CreateAsync(new UxDoc { TenantGuid = tenantB, Number = "FV2026000001" });

        (await store.CountAsync()).Should().Be(2);
    }
}
