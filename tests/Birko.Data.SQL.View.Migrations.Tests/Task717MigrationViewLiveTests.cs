using System;
using System.IO;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.View.Migrations;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.View.Migrations.Tests;

/// <summary>
/// TASK-717, on a real SQLite database. Measured before the fix: <c>CreateView</c> in a migration sent
/// <c>CREATE OR REPLACE VIEW</c>, which SQLite refuses (<c>near "OR": syntax error</c>), so no migration could create a
/// view there; running it again must replace, not fail.
/// </summary>
public sealed class Task717MigrationViewLiveTests : IDisposable
{
    [Table("T717MigCustomers")]
    public class T717MigCustomer : Birko.Data.Models.AbstractModel
    {
        public string Name { get; set; } = null!;
    }

    [Table("T717MigOrders")]
    public class T717MigOrder : Birko.Data.Models.AbstractModel
    {
        public Guid CustomerId { get; set; }
        public decimal Total { get; set; }
    }

    [View(typeof(T717MigCustomer), typeof(T717MigOrder), nameof(T717MigCustomer.Guid), nameof(T717MigOrder.CustomerId), name: "T717MigView")]
    public class T717MigView
    {
        [ViewField(typeof(T717MigCustomer), nameof(T717MigCustomer.Name))]
        public string CustomerName { get; set; } = null!;
    }

    [View(typeof(T717MigCustomer), typeof(T717MigOrder), nameof(T717MigCustomer.Guid), nameof(T717MigOrder.CustomerId), name: "T717MigView")]
    public class T717MigWideView
    {
        [ViewField(typeof(T717MigCustomer), nameof(T717MigCustomer.Name))]
        public string CustomerName { get; set; } = null!;

        [ViewField(typeof(T717MigOrder), nameof(T717MigOrder.Total))]
        public decimal Total { get; set; }
    }

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t717m-{Guid.NewGuid():N}.db");

    [Fact]
    public void A_migration_creates_a_view_on_SQLite_and_replaces_it_when_run_again()
    {
        var settings = new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
        var connector = new SqLiteConnector(settings);
        connector.CreateTable(new[] { typeof(T717MigCustomer), typeof(T717MigOrder) });

        // The connector's connection, as SqlMigrationRunner uses: it registers the collations the tables declare.
        using var connection = connector.CreateConnection(settings);
        connection.Open();
        using (var tx = connection.BeginTransaction())
        {
            var context = new SqlMigrationContext(connection, tx, "sqlite", connector);
            context.CreateView(typeof(T717MigView));
            context.CreateView(typeof(T717MigWideView));
            tx.Commit();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM \"T717MigView\" WHERE 1=0";
        using var reader = command.ExecuteReader();
        reader.FieldCount.Should().Be(2, "the second definition replaced the first");
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_path}"));
        try { File.Delete(_path); } catch { }
    }
}
