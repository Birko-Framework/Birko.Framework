using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-555, found while proving its bulk-update contract: the native bulk update and delete took the primary key from
/// <c>table.GetPrimaryFields()</c>, which has no fallback, while every single-item path uses
/// <c>DataBase.GetPrimaryFields</c>, which falls back to <c>Guid</c> when no <c>[PrimaryField]</c> is declared — the
/// ordinary <c>AbstractModel</c>. For such a model the bulk paths found no key and returned: a bulk update wrote nothing
/// and a bulk delete deleted nothing, with no error, on every SQL provider. Measured here on SQLite before the fix:
/// 0 of 2 rows deleted, a renamed row still read its old name.
///
/// <para>Observed state: the rows the table holds afterwards. <see cref="UpdateMissRow"/> declares no primary field.</para>
/// </summary>
public sealed class BulkPrimaryKeyFallbackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-555pk-" + Guid.NewGuid().ToString("N"));

    public BulkPrimaryKeyFallbackTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void A_bulk_delete_of_a_model_without_a_declared_key_deletes_its_rows()
    {
        var store = new SQLiteStore<UpdateMissRow>();
        store.SetSettings(new SqLiteSettings(_root, "sync.db"));
        var a = new UpdateMissRow { Name = "a" };
        var b = new UpdateMissRow { Name = "b" };
        var keep = new UpdateMissRow { Name = "keep" };
        store.Create(new[] { a, b, keep });

        store.Delete(new[] { a, b });

        store.Read(null, null, null, null).Select(r => r.Name).Should().Equal("keep");
    }

    [Fact]
    public async Task An_async_bulk_delete_of_a_model_without_a_declared_key_deletes_its_rows()
    {
        var store = new AsyncSQLiteStore<UpdateMissRow>();
        store.SetSettings(new SqLiteSettings(_root, "async.db"));
        var a = new UpdateMissRow { Name = "a" };
        var keep = new UpdateMissRow { Name = "keep" };
        await store.CreateAsync(new[] { a, keep });

        await store.DeleteAsync(new[] { a });

        (await store.ReadAsync(null, null, null, null)).Select(r => r.Name).Should().Equal("keep");
    }

    [Fact]
    public void A_bulk_update_of_a_model_without_a_declared_key_writes_its_rows()
    {
        var store = new SQLiteStore<UpdateMissRow>();
        store.SetSettings(new SqLiteSettings(_root, "upd.db"));
        var a = new UpdateMissRow { Name = "a" };
        store.Create(new[] { a });
        a.Name = "a2";

        store.Update(new[] { a });

        store.ReadFirst(x => x.Guid == a.Guid)!.Name.Should().Be("a2");
    }
}
