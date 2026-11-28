using System;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

public class ConditionalDoc : AbstractModel
{
    public string? Name { get; set; }
    public bool Claimed { get; set; }
}

/// <summary>TASK-1013's conditional-update contract on <see cref="AsyncRavenDBStore{T}"/>, live; one database per store.</summary>
public sealed class AsyncRavenConditionalUpdateContractTests : ConditionalUpdateContract<ConditionalDoc>, IDisposable
{
    private readonly RavenLive _live;
    public AsyncRavenConditionalUpdateContractTests(ITestOutputHelper output) => _live = new RavenLive(output);
    protected override bool Ready() => _live.Ready();
    protected override IAsyncBulkStore<ConditionalDoc> NewStore() => _live.Track((url, db) => new AsyncRavenDBStore<ConditionalDoc>(url, db));
    protected override ConditionalDoc NewEntity(string name) => new() { Name = name };
    protected override string NameOf(ConditionalDoc entity) => entity.Name ?? "";
    protected override void Rename(ConditionalDoc entity, string name) => entity.Name = name;
    protected override Expression<Func<ConditionalDoc, bool>> Unclaimed => x => !x.Claimed;
    protected override void Claim(ConditionalDoc entity) => entity.Claimed = true;
    protected override bool IsClaimed(ConditionalDoc entity) => entity.Claimed;
    public void Dispose() => _live.Dispose();
}
