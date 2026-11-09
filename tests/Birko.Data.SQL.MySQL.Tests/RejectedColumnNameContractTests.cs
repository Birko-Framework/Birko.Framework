using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>TASK-862's rejected-column-name contract on <see cref="AsyncMySQLStore{T}"/>, live.</summary>
public sealed class MySQLRejectedColumnNameContractTests : RejectedColumnNameContract
{
    private readonly MySQLLive _live;
    public MySQLRejectedColumnNameContractTests(ITestOutputHelper output) => _live = new MySQLLive(output);
    protected override bool Ready() => _live.Ready();
    protected override string ProviderName => "MySQL";
    protected override RejectedColumnNames Refusals => MySQLConnector.RejectedColumns;
    protected override string ValueFunctionColumn => "CURRENT_USER";
    protected override bool RejectsRank => true;

    protected override IAsyncBulkStore<T> NewStore<T>()
    {
        var store = new AsyncMySQLStore<T>();
        store.SetSettings(_live.Settings());
        return store;
    }

    /// <summary>The list is MySQL 8.4.11's <c>information_schema.KEYWORDS WHERE RESERVED = 1</c>, nothing more.</summary>
    [Fact]
    public void The_list_is_the_servers_reserved_words()
    {
        MySQLConnector.RejectedColumns.Names.Should().HaveCount(262);
        foreach (var name in new[] { "Rank", "Order", "Group", "Key", "Select", "Index", "Table", "Desc" })
            MySQLConnector.RejectedColumns.Contains(name).Should().BeTrue(name);
        foreach (var name in new[] { "Value", "User", "Name", "Status", "Level", "Date", "Text" })
            MySQLConnector.RejectedColumns.Contains(name).Should().BeFalse(name);
    }
}
