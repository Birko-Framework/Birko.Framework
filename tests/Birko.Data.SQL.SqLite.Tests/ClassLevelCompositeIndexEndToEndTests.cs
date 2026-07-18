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
/// End-to-end proof that a class-level [CompositeIndex(IsUnique: true)] whose discriminator column lives on a
/// BASE class (the real consumer shape: TenantGuid on a shared tenant base, the number on the derived entity)
/// emits an enforced CREATE UNIQUE INDEX over (TenantGuid, Number). A duplicate pair is rejected; the same
/// number under a different tenant is allowed.
/// </summary>
public class ClassLevelCompositeIndexEndToEndTests : IDisposable
{
    private readonly string _root;

    public ClassLevelCompositeIndexEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-clux-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    // TenantGuid is declared on the base class — this is exactly what per-property [IndexedField] cannot cover.
    public abstract class TenantDoc : AbstractLogModel
    {
        public Guid TenantGuid { get; set; }
    }

    [Table("ProdOrders")]
    [CompositeIndex("ux_prodorder_docnum", nameof(TenantGuid), nameof(OrderNumber), IsUnique = true)]
    public class ProductionOrder : TenantDoc
    {
        public string OrderNumber { get; set; } = null!;
    }

    private AsyncSQLiteStore<ProductionOrder> NewStore()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "clux.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(ProductionOrder) });

        var store = new AsyncSQLiteStore<ProductionOrder>();
        store.SetSettings(new SqLiteSettings(_root, "clux.db"));
        return store;
    }

    [Fact]
    public async Task ClassLevelCompositeUnique_WithInheritedTenantColumn_IsEnforced()
    {
        var store = NewStore();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await store.CreateAsync(new ProductionOrder { TenantGuid = tenantA, OrderNumber = "PO2026000001" });

        // Same tenant + same number → unique-constraint violation.
        await store.Invoking(s => s.CreateAsync(new ProductionOrder { TenantGuid = tenantA, OrderNumber = "PO2026000001" }))
                   .Should().ThrowAsync<Exception>();

        // Different tenant, same number → allowed (per-tenant uniqueness).
        await store.CreateAsync(new ProductionOrder { TenantGuid = tenantB, OrderNumber = "PO2026000001" });

        (await store.CountAsync()).Should().Be(2);
    }
}
