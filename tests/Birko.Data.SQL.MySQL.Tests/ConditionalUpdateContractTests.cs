using System;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

[Table("T1013ConditionalRows")]
public class ConditionalRow : AbstractModel
{
    public string? Name { get; set; }
    public bool Claimed { get; set; }
}

/// <summary>TASK-1013's conditional-update contract on <see cref="AsyncMySQLStore{T}"/>, live. The table is dropped before each
/// test.</summary>
public sealed class MySQLConditionalUpdateContractTests : ConditionalUpdateContract<ConditionalRow>
{
    private readonly MySQLLive _live;
    public MySQLConditionalUpdateContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<ConditionalRow> NewStore()
    {
        var store = new AsyncMySQLStore<ConditionalRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override ConditionalRow NewEntity(string name) => new() { Name = name };
    protected override string NameOf(ConditionalRow entity) => entity.Name ?? "";
    protected override void Rename(ConditionalRow entity, string name) => entity.Name = name;
    protected override Expression<Func<ConditionalRow, bool>> Unclaimed => x => !x.Claimed;
    protected override void Claim(ConditionalRow entity) => entity.Claimed = true;
    protected override bool IsClaimed(ConditionalRow entity) => entity.Claimed;
}
