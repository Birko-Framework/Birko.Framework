using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.SQL;
using Birko.Data.SQL.Attributes;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.SQL.Tests;

/// <summary>
/// TASK-695. A fluent column can belong to several indexes. One set of index properties held a single index, so a
/// second <c>HasIndex</c> replaced the first silently, and a <c>HasUniqueIndex</c> made every later index on the column
/// UNIQUE.
/// </summary>
public class FluentMultipleIndexTests
{
    [Table("T695Grants")]
    public class Grant : Birko.Data.Models.AbstractModel
    {
        public Guid UserGuid { get; set; }
        public string? ResourceType { get; set; }
        public Guid ResourceId { get; set; }
    }

    private sealed class GrantMap : IModelMapping<Grant>
    {
        public void Configure(ModelMap<Grant> map)
        {
            map.Property(x => x.UserGuid).HasIndex("IX_T695_User", 0);
            map.Property(x => x.ResourceType).HasIndex("IX_T695_User", 1).HasUniqueIndex("UX_T695_Resource", 0);
            map.Property(x => x.ResourceId).HasUniqueIndex("UX_T695_Resource", 1);
        }
    }

    [Table("T695Uniques")]
    public class Unique
    {
        [PrimaryField, NamedField]
        public Guid? Guid { get; set; }
        public string? Code { get; set; }
    }

    private static Birko.Data.SQL.Tables.Table Applied<T>(IModelMapping<T> mapping) where T : class
    {
        var registry = new ModelMapRegistry();
        registry.Register(mapping);
        registry.ApplyToDatabase();
        return DataBase.LoadTable(typeof(T));
    }

    [Fact]
    public void A_column_in_two_composite_indexes_reaches_both_in_its_own_position()
    {
        var table = Applied(new GrantMap());

        table.Indexes.Should().NotBeNull();
        table.Indexes!["IX_T695_User"].Columns.OrderBy(c => c.Order).Select(c => c.ColumnName).Should().Equal("UserGuid", "ResourceType");
        table.Indexes["UX_T695_Resource"].Columns.OrderBy(c => c.Order).Select(c => c.ColumnName).Should().Equal("ResourceType", "ResourceId");
        table.Indexes["UX_T695_Resource"].Columns.Single(c => c.ColumnName == "ResourceType").Order
            .Should().Be(0, "the column's position in this index, not in its first one");
    }

    [Fact]
    public void Uniqueness_belongs_to_the_index_it_was_declared_on()
    {
        var table = Applied(new GrantMap());

        table.Indexes!["IX_T695_User"].Unique.Should().BeFalse("only the second index was declared unique");
        table.Indexes["UX_T695_Resource"].Unique.Should().BeTrue();
    }

    [Fact]
    public void The_shared_column_is_an_index_key_for_rule_37()
    {
        var table = Applied(new GrantMap());

        table.Fields["ResourceType"].IsIndexed.Should().BeTrue("MySQL and SQL Server bound an index-key string instead of TEXT");
    }

    [Fact]
    public void The_same_index_twice_on_one_column_is_refused()
    {
        var map = new ModelMap<Unique>();
        var property = map.Property(x => x.Code).HasIndex("IX_T695_Code");

        var again = () => property.HasIndex("IX_T695_Code", 1);
        var againUnique = () => property.HasUniqueIndex("IX_T695_Code");

        again.Should().Throw<InvalidOperationException>().WithMessage("*Code*IX_T695_Code*");
        againUnique.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_index_needs_a_name()
    {
        var map = new ModelMap<Unique>();

        map.Invoking(m => m.Property(x => x.Code).HasIndex(" ")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_single_index_view_reads_the_first_declaration()
    {
        var map = new ModelMap<Grant>();
        map.Property(x => x.ResourceType).HasIndex("IX_A", 1, descending: true).HasUniqueIndex("UX_B", 0);
        var descriptor = map.Properties.Single(p => p.Name == nameof(Grant.ResourceType));

        descriptor.Indexes.Select(i => i.Name).Should().Equal("IX_A", "UX_B");
        descriptor.IndexName.Should().Be("IX_A");
        descriptor.IndexOrder.Should().Be(1);
        descriptor.IndexDescending.Should().BeTrue();
        descriptor.IndexUnique.Should().BeFalse();
    }

    [Fact]
    public async Task On_SQLite_both_indexes_are_created_and_the_unique_one_refuses_a_duplicate()
    {
        Applied(new GrantMap());
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"birko-t695-{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(root);
        var settings = new Birko.Data.SQL.SqLite.Stores.SqLiteSettings(root, "t695.db");
        try
        {
            var store = new Birko.Data.SQL.SqLite.Stores.AsyncSQLiteStore<Grant>();
            store.SetSettings(settings);
            var resource = System.Guid.NewGuid();
            await store.CreateAsync(new Grant { UserGuid = System.Guid.NewGuid(), ResourceType = "doc", ResourceId = resource });

            var duplicate = () => store.CreateAsync(new Grant { UserGuid = System.Guid.NewGuid(), ResourceType = "doc", ResourceId = resource });

            await duplicate.Should().ThrowAsync<Exception>("(ResourceType, ResourceId) is unique");
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(settings.GetConnectionString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'T695Grants'";
            using var reader = command.ExecuteReader();
            var names = new List<string>();
            while (reader.Read()) names.Add(reader.GetString(0));
            names.Should().Contain("IX_T695_User").And.Contain("UX_T695_Resource");
        }
        finally
        {
            using (var probe = new Microsoft.Data.Sqlite.SqliteConnection(settings.GetConnectionString()))
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(probe);
            try { System.IO.Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
