using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>TASK-862's rejected-column-name contract on <see cref="AsyncPostgreSQLStore{T}"/>, live.</summary>
public sealed class PostgreSQLRejectedColumnNameContractTests : RejectedColumnNameContract
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLRejectedColumnNameContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();
    protected override string ProviderName => "PostgreSQL";
    protected override RejectedColumnNames Refusals => PostgreSQLConnector.RejectedColumns;
    protected override string ValueFunctionColumn => "User";
    protected override bool RejectsRank => false;

    protected override IAsyncBulkStore<T> NewStore<T>()
    {
        var store = new AsyncPostgreSQLStore<T>();
        store.SetSettings(_live.Settings());
        return store;
    }

    /// <summary>PostgreSQL 16.15's catcode R and T keywords plus its six system column names.</summary>
    [Fact]
    public void The_list_is_the_servers_reserved_words_and_system_columns()
    {
        PostgreSQLConnector.RejectedColumns.Names.Should().HaveCount(107);
        foreach (var name in new[] { "Order", "Group", "User", "Select", "Table", "Desc", "Ctid", "Xmin" })
            PostgreSQLConnector.RejectedColumns.Contains(name).Should().BeTrue(name);
        foreach (var name in new[] { "Rank", "Key", "Value", "Index", "Name", "Position" })
            PostgreSQLConnector.RejectedColumns.Contains(name).Should().BeFalse(name);
    }
}
