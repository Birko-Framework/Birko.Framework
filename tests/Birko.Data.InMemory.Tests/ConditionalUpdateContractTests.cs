using System;
using System.Linq.Expressions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.InMemory.Tests;

public class ConditionalRow : AbstractModel
{
    public string? Name { get; set; }
    public bool Claimed { get; set; }
}

/// <summary>TASK-1013's conditional-update contract on <see cref="AsyncInMemoryStore{T}"/>.</summary>
public sealed class AsyncInMemoryConditionalUpdateContractTests : ConditionalUpdateContract<ConditionalRow>
{
    protected override IAsyncBulkStore<ConditionalRow> NewStore() => new AsyncInMemoryStore<ConditionalRow>();
    protected override ConditionalRow NewEntity(string name) => new() { Name = name };
    protected override string NameOf(ConditionalRow entity) => entity.Name ?? "";
    protected override void Rename(ConditionalRow entity, string name) => entity.Name = name;
    protected override Expression<Func<ConditionalRow, bool>> Unclaimed => x => !x.Claimed;
    protected override void Claim(ConditionalRow entity) => entity.Claimed = true;
    protected override bool IsClaimed(ConditionalRow entity) => entity.Claimed;
}
