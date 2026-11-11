using System;
using System.IO;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Tests.Shared.StoreContract;
using Xunit.Abstractions;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-901's view-probe contract on <see cref="SqLiteConnector"/> without <c>Birko.Data.SQL.SqLite.View</c>.</summary>
public sealed class SQLiteViewProbeContractTests : ViewProbeContract
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-901-" + Guid.NewGuid().ToString("N"));
    private readonly SqLiteSettings _settings;

    public SQLiteViewProbeContractTests(ITestOutputHelper output) : base(output)
    {
        Directory.CreateDirectory(_root);
        _settings = new SqLiteSettings(_root, $"t901-{Guid.NewGuid():N}.db");
    }

    protected override string ProviderName => "SQLite";
    protected override bool CreatesViewsWithoutViewPackage => false;
    protected override bool DropsSourceTableUnderAView => true;
    protected override AbstractConnector NewConnector() => new SqLiteConnector(_settings);

    /// <summary>A database file in a directory that does not exist: SQLite cannot open it (error 14).</summary>
    protected override AbstractConnector UnreachableConnector()
        => new SqLiteConnector(new SqLiteSettings(Path.Combine(_root, "missing-dir", "nested"), "t901-unreachable.db"));

    protected override void DisposeProvider()
    {
        SqlitePool.ClearFor(_settings);
        TestTempDirectory.Delete(_root);
    }
}
