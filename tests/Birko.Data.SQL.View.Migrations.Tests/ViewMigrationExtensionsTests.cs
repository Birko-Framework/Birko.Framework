using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.View.Migrations;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.View.Migrations.Tests;

/// <summary>
/// CR-M150: ViewMigrationExtensions had zero coverage. These tests exercise CreateView / DropView
/// (sync + async, by-type and by-name) through a recording fake DbConnection wrapped in a real
/// SqlMigrationContext — asserting the generated CommandText and that the context's transaction is
/// propagated onto the command — plus the wrong-context-type guard and the argument guards.
///
/// A recording fake (not a live SQLite connection) is used because ViewSqlGenerator emits
/// <c>CREATE OR REPLACE VIEW</c>, which SQLite does not accept; the fake lets us assert the DDL and
/// transaction wiring independent of provider dialect (the finding's sanctioned "at minimum" approach).
/// </summary>
public class ViewMigrationExtensionsTests
{
    // ── Decorated view type (base tables resolved from [Table] attributes; no registry needed) ──

    [Table("Customers")]
    public class CustomerModel : Birko.Data.Models.AbstractModel
    {
        public string Name { get; set; } = null!;
    }

    [Table("Orders")]
    public class OrderModel : Birko.Data.Models.AbstractModel
    {
        public Guid CustomerId { get; set; }
        public decimal Total { get; set; }
    }

    [View(typeof(CustomerModel), typeof(OrderModel), nameof(CustomerModel.Guid), nameof(OrderModel.CustomerId), name: "CustomerOrders", connect: ViewConnect.CheckExisting)]
    public class CustomerOrderView
    {
        [ViewField(typeof(CustomerModel), nameof(CustomerModel.Name))]
        public string CustomerName { get; set; } = null!;

        [CountField(typeof(OrderModel), nameof(OrderModel.Guid))]
        public int OrderCount { get; set; }
    }

    private static (SqlMigrationContext ctx, RecordingDbConnection conn, RecordingDbTransaction tx) NewContext()
    {
        var conn = new RecordingDbConnection();
        var tx = new RecordingDbTransaction(conn);
        // TASK-247 made the connector required: it is the only door to the schema builder, whose raw-SQL
        // fallbacks emitted DDL that MySQL and PostgreSQL reject. These tests only record the SQL that reaches
        // the connection, so any connector satisfies the contract — but one has to be supplied.
        var connector = Birko.Data.SQL.DataBase.GetConnector<Birko.Data.SQL.Connectors.SqLiteConnector>(
            new Birko.Data.SQL.SqLite.Stores.SqLiteSettings(
                System.IO.Path.GetTempPath(), $"viewmig-{System.Guid.NewGuid():N}.db"));
        var ctx = new SqlMigrationContext(conn, tx, "sqlite", connector);
        return (ctx, conn, tx);
    }

    [Fact]
    public void CreateView_ByType_EmitsCreateDdl_AndPropagatesTransaction()
    {
        var (ctx, conn, tx) = NewContext();

        ctx.CreateView(typeof(CustomerOrderView));

        conn.LastCommand!.CommandText.Should().StartWith("CREATE OR REPLACE VIEW");
        conn.LastCommand.CommandText.Should().Contain("\"CustomerOrders\"");
        conn.LastCommand.DbTransactionRef.Should().BeSameAs(tx);
        conn.LastCommand.ExecuteNonQueryCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateViewAsync_ByType_EmitsCreateDdl()
    {
        var (ctx, conn, _) = NewContext();

        await ctx.CreateViewAsync(typeof(CustomerOrderView));

        conn.LastCommand!.CommandText.Should().StartWith("CREATE OR REPLACE VIEW");
        conn.LastCommand.ExecuteNonQueryCount.Should().Be(1);
    }

    [Fact]
    public void DropView_ByName_EmitsDropDdl_AndPropagatesTransaction()
    {
        var (ctx, conn, tx) = NewContext();

        ctx.DropView("MyView");

        conn.LastCommand!.CommandText.Should().Be("DROP VIEW IF EXISTS \"MyView\"");
        conn.LastCommand.DbTransactionRef.Should().BeSameAs(tx);
    }

    [Fact]
    public async Task DropViewAsync_ByName_EmitsDropDdl()
    {
        var (ctx, conn, _) = NewContext();

        await ctx.DropViewAsync("MyView");

        conn.LastCommand!.CommandText.Should().Be("DROP VIEW IF EXISTS \"MyView\"");
    }

    [Fact]
    public void DropView_ByType_EmitsDropDdl()
    {
        var (ctx, conn, _) = NewContext();

        ctx.DropView(typeof(CustomerOrderView));

        conn.LastCommand!.CommandText.Should().Be("DROP VIEW IF EXISTS \"CustomerOrders\"");
    }

    [Fact]
    public async Task DropViewAsync_ByType_EmitsDropDdl()
    {
        var (ctx, conn, _) = NewContext();

        await ctx.DropViewAsync(typeof(CustomerOrderView));

        conn.LastCommand!.CommandText.Should().Be("DROP VIEW IF EXISTS \"CustomerOrders\"");
    }

    [Fact]
    public void CustomQuoteChar_IsHonored()
    {
        var (ctx, conn, _) = NewContext();

        ctx.DropView("MyView", '`');

        conn.LastCommand!.CommandText.Should().Be("DROP VIEW IF EXISTS `MyView`");
    }

    [Fact]
    public void NonSqlContext_ThrowsInvalidOperation()
    {
        IMigrationContext ctx = new NonSqlContext();

        Action act = () => ctx.DropView("MyView");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NonSqlContext*");
    }

    [Fact]
    public void NullContext_Throws()
    {
        Action act = () => ViewMigrationExtensions.DropView(null!, "MyView");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NullViewType_Throws()
    {
        var (ctx, _, _) = NewContext();
        Action act = () => ctx.CreateView((Type)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EmptyViewName_Throws()
    {
        var (ctx, _, _) = NewContext();
        Action act = () => ctx.DropView("");
        act.Should().Throw<ArgumentException>();
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class NonSqlContext : IMigrationContext
    {
        public ISchemaBuilder Schema => null!;
        public IDataMigrator Data => null!;
        public string ProviderName => "fake";
        public void Raw(Action<object> providerAction) { }
    }

    private sealed class RecordingDbConnection : DbConnection
    {
        public RecordingDbCommand? LastCommand { get; private set; }

        protected override DbCommand CreateDbCommand()
        {
            LastCommand = new RecordingDbCommand();
            return LastCommand;
        }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => new RecordingDbTransaction(this);
    }

    private sealed class RecordingDbTransaction : DbTransaction
    {
        private readonly DbConnection _conn;
        public RecordingDbTransaction(DbConnection conn) => _conn = conn;
        public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;
        protected override DbConnection DbConnection => _conn;
        public override void Commit() { }
        public override void Rollback() { }
    }

    private sealed class RecordingDbCommand : DbCommand
    {
        public int ExecuteNonQueryCount { get; private set; }
        public DbTransaction? DbTransactionRef => DbTransaction;

        public override int ExecuteNonQuery() { ExecuteNonQueryCount++; return 0; }

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => null!;
        protected override DbTransaction? DbTransaction { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override void Cancel() { }
        public override void Prepare() { }
        public override object? ExecuteScalar() => null;
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }
}
