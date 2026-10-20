using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-691 — a fluent <c>HasMaxLength</c> on an index-key column renders the declared length, not the 255 an
/// unlengthed index-key string gets (rule 37). This is what lets TASK-684 fit the translation key under InnoDB's 3 072
/// bytes. No live server: <c>ConvertType</c> is pure.
/// </summary>
public class FluentLengthColumnTypeTests
{
    public class FluentKeyed : AbstractLogModel
    {
        public string Culture { get; set; } = null!;
        public string Unbounded { get; set; } = null!;
    }

    private sealed class FluentKeyedMap : IModelMapping<FluentKeyed>
    {
        public void Configure(ModelMap<FluentKeyed> map)
        {
            map.ToTable("MyFluentKeyed");
            map.Property(x => x.Culture).HasMaxLength(85).HasUniqueIndex("UX_MyFluentKeyed_Key", 0);
            map.Property(x => x.Unbounded).HasUniqueIndex("UX_MyFluentKeyed_Key", 1);
        }
    }

    private static string TypeOf(string property)
    {
        var registry = new ModelMapRegistry();
        registry.Register(new FluentKeyedMap());
        registry.ApplyToDatabase();
        var field = Birko.Data.SQL.DataBase.LoadTable(typeof(FluentKeyed)).Fields.Values.First(f => f.Property?.Name == property);
        return new MySQLConnector(new MySqlSettings("localhost", "db", "user", "pass")).ConvertType(field.Type, field);
    }

    [Fact]
    public void A_fluent_length_on_an_index_key_column_is_rendered_as_declared()
        => TypeOf(nameof(FluentKeyed.Culture)).Should().Be("VARCHAR(85)");

    [Fact]
    public void An_index_key_column_without_one_keeps_the_rule_37_bound()
        => TypeOf(nameof(FluentKeyed.Unbounded)).Should().Be("VARCHAR(255)");
}
