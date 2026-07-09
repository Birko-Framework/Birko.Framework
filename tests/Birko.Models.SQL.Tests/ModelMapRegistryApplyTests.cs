using System;
using System.Linq;
using Birko.Data.SQL.Attributes;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.SQL.Tests;

/// <summary>
/// CR-H132: ApplyToDatabase previously propagated only the four boolean flags and silently dropped
/// HasColumnName (even though the SQL field name is settable). It now applies the column name; the
/// length/precision/scale/index facets remain mapping-metadata-only (documented, still readable via
/// GetPropertyMaps). LoadTable is pure reflection, so this runs without a database connection.
/// </summary>
public class ModelMapRegistryApplyTests
{
    [Table("test_widgets")]
    public class Widget
    {
        [PrimaryField, NamedField]
        public Guid? Guid { get; set; }

        [NamedField]
        public string Code { get; set; } = string.Empty;

        [NamedField]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class WidgetMap : IModelMapping<Widget>
    {
        public void Configure(ModelMap<Widget> map)
        {
            map.ToTable("test_widgets");
            map.Property(x => x.Code).HasColumnName("code_col").IsUnique().HasMaxLength(64);
            map.Property(x => x.Name).IsRequired();
        }
    }

    [Fact]
    public void ApplyToDatabase_AppliesColumnNameAndFlags()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new WidgetMap());

        registry.ApplyToDatabase();

        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(Widget));
        var codeField = table.GetFieldByPropertyName("Code");
        var nameField = table.GetFieldByPropertyName("Name");

        codeField.Should().NotBeNull();
        codeField!.Name.Should().Be("code_col", "HasColumnName must reach the SQL field name");
        codeField.IsUnique.Should().BeTrue();
        nameField!.IsNotNull.Should().BeTrue("IsRequired maps to IsNotNull");
    }

    [Fact]
    public void MappingLayer_RetainsMetadataThatIsNotApplied()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new WidgetMap());

        // HasMaxLength is not applied to the schema, but stays readable via the mapping metadata.
        var codeDescriptor = registry.GetPropertyMaps(typeof(Widget)).First(f => f.Name == "Code");
        codeDescriptor.MaxLength.Should().Be(64);
        codeDescriptor.ColumnName.Should().Be("code_col");
    }

    [Fact]
    public void GetTableNames_ExposesConfiguredTable()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new WidgetMap());

        registry.GetTableNames().Should().Contain(kvp => kvp.Key == typeof(Widget) && kvp.Value == "test_widgets");
    }
}
