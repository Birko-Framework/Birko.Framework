using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tenant.Tests;

/// <summary>
/// TASK-826, the shape Symbio TASK-1063 needs: a tax rate changes, so every price row with that rate gets the new rate
/// (a constant) and its net price recomputed from its gross price, through tenant → soft-delete → timestamp over a
/// native SQL store. One UPDATE statement; only the current tenant's live rows change; <c>UpdatedAt</c> is stamped.
/// </summary>
public class Task826ComputedSetChainTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-tenant-826-{Guid.NewGuid():N}");

    public void Dispose() => TestTempDirectory.Delete(_root);

    [Table("T826Prices")]
    public class BasePrice : AbstractModel, ITenant, ISoftDeletable, ITimestamped
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public Guid TaxRateGuid { get; set; }
        [PrecisionField(18)]
        [ScaleField(4)]
        public decimal TaxRatePercent { get; set; }
        [PrecisionField(18)]
        [ScaleField(4)]
        public decimal PriceWithTax { get; set; }
        [PrecisionField(18)]
        [ScaleField(4)]
        public decimal PriceWithoutTax { get; set; }
        public DateTime? DeletedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? PrevUpdatedAt { get; set; }
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Now;
        public DateTimeOffset OffsetUtcNow => new(Now);
        public DateOnly Today => DateOnly.FromDateTime(Now);
    }

    private static readonly Guid Ours = Guid.NewGuid();
    private static readonly Guid Theirs = Guid.NewGuid();
    private static readonly Guid Rate = Guid.NewGuid();

    private static TenantContext CtxFor(Guid tenant)
    {
        var ctx = new TenantContext();
        ctx.SetTenant(tenant, tenant == Ours ? "Ours" : "Theirs");
        return ctx;
    }

    private static BasePrice Row(Guid tenant, decimal gross, DateTime? deletedAt = null) => new()
    {
        Guid = Guid.NewGuid(), TenantGuid = tenant, TenantName = tenant == Ours ? "Ours" : "Theirs", TaxRateGuid = Rate,
        TaxRatePercent = 20m, PriceWithTax = gross, PriceWithoutTax = 0m, DeletedAt = deletedAt,
    };

    [Fact]
    public async Task A_rate_change_recomputes_the_current_tenants_live_prices_in_one_statement()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "chain.db" });
        ((SqLiteConnector)factory.GetConnector()).CreateTable(new[] { typeof(BasePrice) });
        var store = new AsyncSQLiteStore<BasePrice>();
        store.SetSettings(new SqLiteSettings(_root, "chain.db"));
        var midpoint = Row(Ours, 0.15m);
        var plain = Row(Ours, 120m);
        var deleted = Row(Ours, 0.15m, FixedClock.Now.AddDays(-1));
        var foreign = Row(Theirs, 0.15m);
        foreach (var row in new[] { midpoint, plain, deleted, foreign }) await store.CreateAsync(row);

        var statements = new ConcurrentQueue<string>();
        store.Connector!.OnExecute += text => statements.Enqueue(text);
        var chain = new AsyncTenantBulkStoreWrapper<IAsyncBulkStore<BasePrice>, BasePrice>(
            new AsyncSoftDeleteBulkStoreWrapper<IAsyncBulkStore<BasePrice>, BasePrice>(
                new AsyncTimestampBulkStoreWrapper<AsyncSQLiteStore<BasePrice>, BasePrice>(store, new FixedClock()), new FixedClock()),
            CtxFor(Ours));

        var newRate = 20m;
        await chain.UpdateAsync(x => x.TaxRateGuid == Rate, new PropertyUpdate<BasePrice>()
            .Set(x => x.TaxRatePercent, newRate)
            .Compute(x => x.PriceWithoutTax, x => Math.Round(x.PriceWithTax / (1 + newRate / 100m), 2, MidpointRounding.ToEven)));

        statements.Where(s => s.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            .Should().ContainSingle("the rate, the recomputed price and the timestamp are one statement, not a read-modify-save per row");

        async Task<BasePrice> Read(Guid guid) => (await store.ReadAsync(x => x.Guid == guid)).Single();
        (await Read(midpoint.Guid!.Value)).PriceWithoutTax.Should().Be(0.12m, "0.15 / 1.2 = 0.125 rounds half to even");
        (await Read(plain.Guid!.Value)).PriceWithoutTax.Should().Be(100m);
        (await Read(midpoint.Guid!.Value)).UpdatedAt.Should().Be(FixedClock.Now, "the timestamp decorator's own Set lands beside the formula");
        (await Read(deleted.Guid!.Value)).PriceWithoutTax.Should().Be(0m, "a soft-deleted row is outside the update");
        (await Read(foreign.Guid!.Value)).PriceWithoutTax.Should().Be(0m, "another tenant's row is outside the update");
    }

    [Fact]
    public void Sync_chain_recomputes_only_the_current_tenants_rows()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "sync.db" });
        ((SqLiteConnector)factory.GetConnector()).CreateTable(new[] { typeof(BasePrice) });
        var store = new SQLiteStore<BasePrice>();
        store.SetSettings(new SqLiteSettings(_root, "sync.db"));
        var ours = Row(Ours, 0.15m);
        var theirs = Row(Theirs, 0.15m);
        store.Create(ours);
        store.Create(theirs);

        new TenantBulkStoreWrapper<SQLiteStore<BasePrice>, BasePrice>(store, CtxFor(Ours))
            .Update(x => x.TaxRateGuid == Rate, new PropertyUpdate<BasePrice>()
                .Compute(x => x.PriceWithoutTax, x => Math.Round(x.PriceWithTax / 1.2m, 2, MidpointRounding.AwayFromZero)));

        store.ReadFirst(x => x.Guid == ours.Guid)!.PriceWithoutTax.Should().Be(0.13m);
        store.ReadFirst(x => x.Guid == theirs.Guid)!.PriceWithoutTax.Should().Be(0m);
    }
}
