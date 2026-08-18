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

    /// <remarks>
    /// <b>Portability note (TASK-248).</b> <c>Number</c> is a plain <c>string</c> with no declared length, and
    /// this is the example consumers copy. On MySQL an unbounded string maps to <c>LONGTEXT</c>, which
    /// <b>cannot be indexed without a key length</b> (ERROR 1170) — so for a while this shape produced no index
    /// and, for a UNIQUE one, no constraint on that provider, recorded and invisible.
    /// <para>
    /// It works everywhere now: <c>MySQLConnector.ConvertType</c> bounds an <i>indexed</i> string to
    /// <c>VARCHAR(255)</c>. Declaring <c>[MaxLengthField(n)]</c> explicitly is still preferable — it is
    /// portable, visible at the model, and applies on every provider rather than relying on one connector's
    /// default. This model deliberately stays unbounded so the automatic path keeps being exercised by the
    /// example people actually copy.
    /// </para>
    /// </remarks>
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
