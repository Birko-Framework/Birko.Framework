using System;
using System.Linq.Expressions;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

public class ConditionalDoc : AbstractModel
{
    public string? Name { get; set; }
    public bool Claimed { get; set; }
}

/// <summary>TASK-1013's conditional-update contract on <see cref="AsyncCosmosDBStore{T}"/>, live; one database per store.</summary>
public sealed class AsyncCosmosConditionalUpdateContractTests : ConditionalUpdateContract<ConditionalDoc>, IDisposable
{
    private readonly CosmosLive _live;
    public AsyncCosmosConditionalUpdateContractTests(ITestOutputHelper output) => _live = new CosmosLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<ConditionalDoc> NewStore()
    {
        var (conn, db, settings) = _live.Next();
        return new AsyncCosmosDBStore<ConditionalDoc>(conn, db, null, settings);
    }

    protected override ConditionalDoc NewEntity(string name) => new() { Name = name };
    protected override string NameOf(ConditionalDoc entity) => entity.Name ?? "";
    protected override void Rename(ConditionalDoc entity, string name) => entity.Name = name;
    protected override Expression<Func<ConditionalDoc, bool>> Unclaimed => x => !x.Claimed;
    protected override void Claim(ConditionalDoc entity) => entity.Claimed = true;
    protected override bool IsClaimed(ConditionalDoc entity) => entity.Claimed;
    public void Dispose() => _live.Dispose();
}
