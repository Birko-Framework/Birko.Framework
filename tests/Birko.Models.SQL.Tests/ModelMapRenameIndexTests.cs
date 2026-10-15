using System;
using System.Linq;
using Birko.Data.SQL.Attributes;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.SQL.Tests;

/// <summary>
/// SH-M243 (TASK-154). A table is keyed, and its attribute-declared indexes resolved, by column name at load.
/// <c>HasColumnName</c> renamed the field in place afterwards, so <c>Fields</c> stayed keyed by the old name and the
/// index still named a column the table no longer had.
/// </summary>
public class ModelMapRenameIndexTests
{
    [Table("test_renamed_indexed")]
    public class Part
    {
        [PrimaryField, NamedField]
        public Guid? Guid { get; set; }

        [IndexedField("IX_Part_Sku")]
        public string Sku { get; set; } = string.Empty;
    }

    private sealed class PartMap : IModelMapping<Part>
    {
        public void Configure(ModelMap<Part> map) => map.Property(x => x.Sku).HasColumnName("sku_code");
    }

    [Fact]
    public void A_renamed_column_is_the_key_of_its_field_and_the_column_of_its_index()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new PartMap());
        registry.ApplyToDatabase();

        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(Part));
        table.Fields.Keys.Should().Contain("sku_code").And.NotContain("Sku");
        table.Indexes!["IX_Part_Sku"].Columns.Single().ColumnName.Should().Be("sku_code");
    }
}
