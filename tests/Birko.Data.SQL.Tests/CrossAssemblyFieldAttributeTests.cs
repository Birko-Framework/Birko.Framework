extern alias foreign;

using System;
using System.Linq;
using Birko.Data.Exceptions;
using Birko.Data.SQL;
using Birko.Data.SQL.Fields;
using FluentAssertions;
using Xunit;
using ForeignModel = foreign::Birko.Data.SQL.Tests.ForeignAttributes.ForeignModel;
using NewerModel = foreign::Birko.Data.SQL.Tests.ForeignAttributes.NewerModel;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-577. In the split-aggregator layout a model's SQL attributes come from another assembly, and
/// <c>Birko.Data.SQL.DataBase.LoadField</c> rebuilds them by type name. The rebuild read <c>MaxLength</c>, <c>Precision</c> and
/// <c>Scale</c> as properties although they are fields (each rebuilt as 0), and skipped <c>[UtcField]</c>. The
/// attributes here really are of a foreign identity: <c>Birko.Data.SQL.Tests.ForeignAttributes</c> compiles the same
/// attribute sources into a second assembly.
/// </summary>
public class CrossAssemblyFieldAttributeTests
{
    private static AbstractField Field(string property)
        => Birko.Data.SQL.DataBase.LoadField(typeof(ForeignModel).GetProperty(property)!).Single();

    [Fact]
    public void The_attributes_really_are_foreign()
    {
        var attr = typeof(ForeignModel).GetProperty(nameof(ForeignModel.Name))!.GetCustomAttributes(true).Single();
        attr.GetType().FullName.Should().Be("Birko.Data.SQL.Attributes.MaxLengthField");
        (attr is Birko.Data.SQL.Attributes.Field).Should().BeFalse();
    }

    [Fact]
    public void A_foreign_max_length_keeps_its_length()
        => Field(nameof(ForeignModel.Name)).Should().BeOfType<CharField>().Which.Lenght.Should().Be(64);

    [Fact]
    public void A_foreign_precision_and_scale_keep_their_values()
    {
        var field = Field(nameof(ForeignModel.Amount)).Should().BeOfType<DecimalField>().Subject;
        field.Precision.Should().Be(18);
        field.Scale.Should().Be(4);
    }

    [Fact]
    public void A_foreign_utc_field_stays_an_instant()
        => Field(nameof(ForeignModel.At)).Should().BeOfType<UtcDateTimeField>();

    [Fact]
    public void The_flag_attributes_keep_their_meaning()
    {
        Field(nameof(ForeignModel.Id)).IsPrimary.Should().BeTrue();
        Field(nameof(ForeignModel.Id)).IsNotNull.Should().BeTrue();
        Field(nameof(ForeignModel.Email)).IsUnique.Should().BeTrue();
        Field(nameof(ForeignModel.Sequence)).IsAutoincrement.Should().BeTrue();
        Field(nameof(ForeignModel.Renamed)).Name.Should().Be("renamed_column");
        Birko.Data.SQL.DataBase.LoadField(typeof(ForeignModel).GetProperty(nameof(ForeignModel.NotStored))!).Should().BeEmpty();
    }

    [Fact]
    public void A_foreign_table_keeps_its_indexes_unique_and_filtered()
    {
        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(ForeignModel));
        table.Name.Should().Be("ForeignAttributed");

        var code = table.Indexes!["UX_Foreign_Code"];
        code.Unique.Should().BeTrue();
        code.Columns.Single().IsDescending.Should().BeTrue();
        code.Predicates.Should().ContainSingle(p => p.ColumnName == "Code" && !p.RequireNull);

        table.Indexes!["UX_Foreign_Pair"].Unique.Should().BeTrue();
    }

    [Fact]
    public void An_attribute_this_build_cannot_rebuild_is_refused_by_name()
    {
        var act = () => Birko.Data.SQL.DataBase.LoadField(typeof(NewerModel).GetProperty(nameof(NewerModel.Value))!);
        act.Should().Throw<FieldAttributeException>().WithMessage("*Birko.Data.SQL.Attributes.NewerField*");
    }

    /// <summary>
    /// The guard on the next attribute: every concrete field attribute this build declares appears on
    /// <see cref="ForeignModel"/>, so one added without a rebuild case fails the round-trip tests above.
    /// </summary>
    [Fact]
    public void Every_field_attribute_type_is_exercised_cross_assembly()
    {
        var declared = typeof(Birko.Data.SQL.Attributes.Field).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Birko.Data.SQL.Attributes.Field)) && !t.IsAbstract
                     && t.Namespace == "Birko.Data.SQL.Attributes")
            .Select(t => t.Name);
        var exercised = typeof(ForeignModel).GetProperties()
            .SelectMany(p => p.GetCustomAttributes(true))
            .Select(a => a.GetType().Name)
            .Distinct();

        exercised.Should().Contain(declared);
    }
}
