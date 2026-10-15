using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Composition;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using Birko.Data.Tenant.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Composition.Tests;

/// <summary>
/// TASK-631 (SH-M336): a store composed by <see cref="StoreWrapperBuilder"/> keeps the raw store's capabilities. An
/// aggregate honours every decorator's scope; a transaction context and settings reach the raw store.
/// </summary>
public class StoreChainCapabilityTests
{
    public class Sale : AbstractModel, ITenant, ISoftDeletable
    {
        public Guid TenantGuid { get; set; }
        public string? TenantName { get; set; }
        public DateTime? DeletedAt { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>A raw store that also takes a transaction context and string settings, to see what reaches it.</summary>
    public class RecordingStore : AsyncInMemoryStore<Sale>, IAsyncTransactionalStore<Sale, string>, ISettingsStore<string>
    {
        public string? TransactionContext { get; private set; }
        public void SetTransactionContext(string? context) => TransactionContext = context;
        public string? LastSettings { get; private set; }
        public void SetSettings(string settings) => LastSettings = settings;
    }

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static async Task<RecordingStore> Seeded()
    {
        var raw = new RecordingStore();
        await raw.CreateAsync(new[]
        {
            new Sale { TenantGuid = TenantA, Amount = 1 },
            new Sale { TenantGuid = TenantA, Amount = 2 },
            new Sale { TenantGuid = TenantA, Amount = 4, DeletedAt = DateTime.UtcNow },
            new Sale { TenantGuid = TenantB, Amount = 8 },
        });
        return raw;
    }

    private static AggregateQuery<Sale> Totals() => new()
    {
        Aggregates = new[] { new AggregateField(AggregateFunction.Sum, nameof(Sale.Amount), "total"), new AggregateField(AggregateFunction.Count, nameof(Sale.Amount), "rows") },
    };

    [Fact]
    public async Task An_aggregate_through_the_chain_honours_soft_delete_and_tenant_scope()
    {
        var raw = await Seeded();
        var tenant = new TenantContext();
        tenant.SetTenant(TenantA);
        var chain = StoreWrapperBuilder.Build<Sale>(raw, tenantContext: tenant);

        var aggregatable = chain as IAsyncAggregatableStore<Sale>;
        aggregatable.Should().NotBeNull("the chain keeps the raw store's aggregation");

        var result = (await aggregatable!.AggregateAsync(Totals())).Single();

        Convert.ToDecimal(result.Values["total"]).Should().Be(3, "tenant A's live rows only: not the deleted 4, not tenant B's 8");
        Convert.ToInt64(result.Values["rows"]).Should().Be(2);
    }

    [Fact]
    public async Task A_caller_filter_is_kept_and_narrowed()
    {
        var raw = await Seeded();
        var tenant = new TenantContext();
        tenant.SetTenant(TenantA);
        var chain = (IAsyncAggregatableStore<Sale>)StoreWrapperBuilder.Build<Sale>(raw, tenantContext: tenant);

        var query = Totals().WithFilter(x => x.Amount > 1);
        Convert.ToDecimal((await chain.AggregateAsync(query)).Single().Values["total"]).Should().Be(2);
    }

    [Fact]
    public async Task A_strict_chain_with_no_tenant_refuses_the_aggregate()
    {
        var raw = await Seeded();
        var chain = (IAsyncAggregatableStore<Sale>)StoreWrapperBuilder.Build<Sale>(raw, tenantContext: new TenantContext(),
            tenantMode: TenantIsolationMode.Strict);

        await chain.Invoking(c => c.AggregateAsync(Totals())).Should().ThrowAsync<Exception>()
            .Where(e => e.GetType().Name == "TenantScopeRequiredException");
    }

    [Fact]
    public async Task A_transaction_context_and_settings_reach_the_raw_store()
    {
        var raw = await Seeded();
        var tenant = new TenantContext();
        tenant.SetTenant(TenantA);
        var chain = StoreWrapperBuilder.Build<Sale>(raw, tenantContext: tenant);

        StoreChain.SetTransactionContext<Sale, string>(chain, "tx-1");
        StoreChain.SetSettings(chain, "conn");

        raw.TransactionContext.Should().Be("tx-1");
        raw.LastSettings.Should().Be("conn");
        StoreChain.Walk(chain).Last().Should().BeSameAs(raw);
        FluentActions.Invoking(() => StoreChain.SetTransactionContext<Sale, int>(chain, 5)).Should().Throw<NotSupportedException>();
    }
}
