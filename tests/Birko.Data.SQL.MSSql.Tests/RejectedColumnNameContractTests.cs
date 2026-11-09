using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>TASK-862's rejected-column-name contract on <see cref="AsyncMSSqlStore{T}"/>, live.</summary>
public sealed class SQLServerRejectedColumnNameContractTests : RejectedColumnNameContract
{
    private readonly SQLServerLive _live;
    public SQLServerRejectedColumnNameContractTests(ITestOutputHelper output) => _live = new SQLServerLive(output);
    protected override bool Ready() => _live.Ready();
    protected override string ProviderName => "SQL Server";
    protected override RejectedColumnNames Refusals => MSSqlConnector.RejectedColumns;
    protected override string ValueFunctionColumn => "User";
    protected override bool RejectsRank => false;

    protected override IAsyncBulkStore<T> NewStore<T>()
    {
        var store = new AsyncMSSqlStore<T>();
        store.SetSettings(_live.Settings());
        return store;
    }

    /// <summary>SQL Server has no RENAME COLUMN; sp_rename takes the new name unquoted.</summary>
    protected override string RenameToRejectedSql(AbstractConnector connector, string table, string from, string to)
        => connector.RenameColumnSql(table, from, to, quoteColumns: false);

    /// <summary>The documented reserved keywords SQL Server 2022 rejected: 179 of 185.</summary>
    [Fact]
    public void The_list_is_the_reserved_words_the_server_rejects()
    {
        MSSqlConnector.RejectedColumns.Names.Should().HaveCount(179);
        foreach (var name in new[] { "Order", "Group", "Key", "User", "Select", "Index", "Table", "Desc" })
            MSSqlConnector.RejectedColumns.Contains(name).Should().BeTrue(name);
        foreach (var name in new[] { "Rank", "Value", "Name", "Disk", "Dump", "Load", "Precision", "Within" })
            MSSqlConnector.RejectedColumns.Contains(name).Should().BeFalse(name);
    }
}
