using System;
using System.Collections.Generic;
using System.IO;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-862's rejected-column-name contract on <see cref="AsyncSQLiteStore{T}"/>.</summary>
public sealed class SQLiteRejectedColumnNameContractTests : RejectedColumnNameContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-862-" + Guid.NewGuid().ToString("N"));
    private readonly SqLiteSettings _settings;

    public SQLiteRejectedColumnNameContractTests()
    {
        Directory.CreateDirectory(_root);
        _settings = new SqLiteSettings(_root, $"t862-{Guid.NewGuid():N}.db");
    }

    protected override string ProviderName => "SQLite";
    protected override RejectedColumnNames Refusals => SqLiteConnector.RejectedColumns;
    protected override bool RejectsRank => false;

    protected override IReadOnlyCollection<string> RejectedOnlyInWrites { get; } =
        new[] { "CAST", "RAISE", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP" };

    protected override IAsyncBulkStore<T> NewStore<T>()
    {
        var store = new AsyncSQLiteStore<T>();
        store.SetSettings(_settings);
        return store;
    }

    /// <summary>The 58 names SQLite 3.53.3 rejects in DDL, plus the five it accepts there and fails or misreads in a write.</summary>
    [Fact]
    public void The_list_is_what_the_engine_rejects_not_its_keyword_list()
    {
        SqLiteConnector.RejectedColumns.Names.Should().HaveCount(63);
        foreach (var name in new[] { "Order", "Group", "Select", "Index", "Table", "Current_Date", "Cast" })
            SqLiteConnector.RejectedColumns.Contains(name).Should().BeTrue(name);
        foreach (var name in new[] { "Rank", "Key", "Value", "User", "Desc", "Asc", "Like", "Left", "Rowid" })
            SqLiteConnector.RejectedColumns.Contains(name).Should().BeFalse(name);
    }

    public void Dispose()
    {
        SqlitePool.ClearFor(_settings);
        TestTempDirectory.Delete(_root);
    }
}
