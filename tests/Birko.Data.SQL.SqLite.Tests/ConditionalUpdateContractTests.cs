using System;
using System.IO;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.SQL.SqLite.Tests;

[Table("T1013ConditionalRows")]
public class ConditionalRow : AbstractModel
{
    public string? Name { get; set; }
    public bool Claimed { get; set; }
}

/// <summary>TASK-1013's conditional-update contract on <see cref="AsyncSQLiteStore{T}"/>, one database file per store.</summary>
public sealed class SQLiteConditionalUpdateContractTests : ConditionalUpdateContract<ConditionalRow>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-1013-" + Guid.NewGuid().ToString("N"));

    public SQLiteConditionalUpdateContractTests() => Directory.CreateDirectory(_root);

    protected override IAsyncBulkStore<ConditionalRow> NewStore()
    {
        var store = new AsyncSQLiteStore<ConditionalRow>();
        store.SetSettings(new SqLiteSettings(_root, $"t1013-{Guid.NewGuid():N}.db"));
        return store;
    }

    protected override ConditionalRow NewEntity(string name) => new() { Name = name };
    protected override string NameOf(ConditionalRow entity) => entity.Name ?? "";
    protected override void Rename(ConditionalRow entity, string name) => entity.Name = name;
    protected override Expression<Func<ConditionalRow, bool>> Unclaimed => x => !x.Claimed;
    protected override void Claim(ConditionalRow entity) => entity.Claimed = true;
    protected override bool IsClaimed(ConditionalRow entity) => entity.Claimed;

    public void Dispose() => TestTempDirectory.Delete(_root);
}
