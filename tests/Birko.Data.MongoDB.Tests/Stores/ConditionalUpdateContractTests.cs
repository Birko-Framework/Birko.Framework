using System;
using System.Linq.Expressions;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

public class ConditionalDoc : Birko.Data.MongoDB.Models.MongoDBModel
{
    public string? Name { get; set; }
    public bool Claimed { get; set; }
}

/// <summary>TASK-1013's conditional-update contract on <see cref="AsyncMongoDBStore{T}"/>, live; one database per store.</summary>
public sealed class AsyncMongoConditionalUpdateContractTests : ConditionalUpdateContract<ConditionalDoc>, IDisposable
{
    private readonly MongoLive _live;
    public AsyncMongoConditionalUpdateContractTests(ITestOutputHelper output) => _live = new MongoLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<ConditionalDoc> NewStore()
    {
        var store = new AsyncMongoDBStore<ConditionalDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override ConditionalDoc NewEntity(string name) => new() { Name = name };
    protected override string NameOf(ConditionalDoc entity) => entity.Name ?? "";
    protected override void Rename(ConditionalDoc entity, string name) => entity.Name = name;
    protected override Expression<Func<ConditionalDoc, bool>> Unclaimed => x => !x.Claimed;
    protected override void Claim(ConditionalDoc entity) => entity.Claimed = true;
    protected override bool IsClaimed(ConditionalDoc entity) => entity.Claimed;
    public void Dispose() => _live.Dispose();
}
