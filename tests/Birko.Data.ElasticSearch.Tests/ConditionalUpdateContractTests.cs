using System;
using System.Linq.Expressions;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

public class ConditionalDoc : AbstractModel
{
    public string? Name { get; set; }
    public bool Claimed { get; set; }
}

/// <summary>TASK-1013's conditional-update contract on <see cref="AsyncElasticSearchStore{T}"/>, live; one index per store.</summary>
public sealed class AsyncElasticConditionalUpdateContractTests : ConditionalUpdateContract<ConditionalDoc>, IDisposable
{
    private readonly ElasticLive _live;
    public AsyncElasticConditionalUpdateContractTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<ConditionalDoc> NewStore()
    {
        var store = new AsyncElasticSearchStore<ConditionalDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override void Settle() => ElasticLive.Refresh();
    protected override ConditionalDoc NewEntity(string name) => new() { Name = name };
    protected override string NameOf(ConditionalDoc entity) => entity.Name ?? "";
    protected override void Rename(ConditionalDoc entity, string name) => entity.Name = name;
    protected override Expression<Func<ConditionalDoc, bool>> Unclaimed => x => !x.Claimed;
    protected override void Claim(ConditionalDoc entity) => entity.Claimed = true;
    protected override bool IsClaimed(ConditionalDoc entity) => entity.Claimed;
    public void Dispose() => _live.Dispose();
}
