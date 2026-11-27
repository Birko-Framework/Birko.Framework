using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.InMemory.Stores;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.EventSourcing;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.EventSourcing.SQL.Tests;

/// <summary>
/// TASK-995 — the shared event store contract on <see cref="SqlEventStore{DB}"/>, obtained the way an application does:
/// <c>factory.GetEventStore(o => o.AddSql())</c> on a SQLite factory. A new factory per writer, as a second process has.
/// </summary>
public sealed class SqlEventStoreContractTests : EventStoreContract, IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "birko_t995_" + Guid.NewGuid().ToString("N"));

    public SqlEventStoreContractTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        TestTempDirectory.Delete(_dir);
    }

    private IStoreFactory NewFactory()
        => Birko.Data.Stores.StoreFactory.Create("SqLite", $"Data Source={Path.Combine(_dir, "events.db")}", p => p.AddSqLite());

    protected override Task<IAsyncEventStore> NewStoreAsync() => Task.FromResult(NewFactory().GetEventStore(o => o.AddSql()));

    [Fact]
    public void A_SQL_factory_answers_SqlEventStore_over_its_own_connector()
        => NewFactory().GetEventStore(o => o.AddSql()).Should().BeOfType<SqlEventStore<SqLiteConnector>>();

    [Fact]
    public void A_non_SQL_factory_is_not_taken_by_the_SQL_override()
        => Birko.Data.Stores.StoreFactory.Create("InMemory", null, p => p.AddInMemory())
            .GetEventStore(o => o.AddSql()).Should().BeOfType<AsyncStoreEventStore>();
}
