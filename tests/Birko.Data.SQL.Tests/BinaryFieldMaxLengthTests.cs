using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Fields;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-266 — <c>BinaryField</c> can carry a declared width, and the mapper delivers one.
///
/// <para>
/// This lives in <c>Birko.Data.SQL.Tests</c> rather than only in the provider suites because
/// <c>BinaryField</c> and <c>CreateAbstractField</c> are declared in <c>Birko.Data.SQL</c> — § Testing's
/// rule that a project's surface is tested in its own <c>.Tests</c> project. TASK-257's close gate caught
/// the identical omission for <c>AbstractField.IsInIndexKey</c>, which was covered only from two provider
/// suites: a guard whose only coverage lives downstream is one a downstream cleanup can delete silently.
/// </para>
/// <para>
/// <b>What was missing.</b> <c>BinaryField</c> had no length at all and <c>CreateAbstractField</c> never
/// passed <c>maxLength</c> for a <c>byte[]</c>, so <c>[MaxLengthField(32)]</c> on one was <b>silently
/// dropped</b>. That mattered beyond column width: neither SQL Server nor MySQL can index an unbounded
/// blob, so "declare a width" was the natural remedy to recommend and it was not expressible — § TASK-263's
/// <i>"named an escape hatch that did not open"</i>.
/// </para>
/// </summary>
public class BinaryFieldMaxLengthTests
{
    [Table("SqlBinFieldPlain")]
    public class PlainEntity : AbstractLogModel
    {
        public byte[] Blob { get; set; } = Array.Empty<byte>();

        [MaxLengthField(32)]
        public byte[] Sha256 { get; set; } = Array.Empty<byte>();

        [PrecisionField(48)]
        public byte[] ViaPrecision { get; set; } = Array.Empty<byte>();
    }

    private static BinaryField BinaryFieldFor(string property)
    {
        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(PlainEntity));
        var field = table.Fields.Values.FirstOrDefault(f => f.Property?.Name == property);
        field.Should().NotBeNull($"'{property}' must map to a column at all");
        return field.Should().BeOfType<BinaryField>(
            "a byte[] property maps to BinaryField, and the width lives on that type").Subject;
    }

    [Fact]
    public void A_byte_array_with_no_attribute_has_no_declared_width()
        => BinaryFieldFor(nameof(PlainEntity.Blob)).MaxLength.Should().BeNull(
            "null is what tells a connector to emit its unbounded blob type");

    [Fact]
    public void MaxLengthField_reaches_a_byte_array_column()
        => BinaryFieldFor(nameof(PlainEntity.Sha256)).MaxLength.Should().Be(32,
            "before TASK-266 this attribute was silently discarded for byte[]");

    /// <summary>
    /// The same <c>MaxLength</c>-then-<c>Precision</c> fallback the string arm has carried for backwards
    /// compatibility, so one rule covers both reference-typed widths rather than two that can drift.
    /// </summary>
    [Fact]
    public void The_width_falls_back_to_PrecisionField_as_it_does_for_strings()
        => BinaryFieldFor(nameof(PlainEntity.ViaPrecision)).MaxLength.Should().Be(48);

    /// <summary>
    /// A hand-constructed field defaults to no width, so the default is the field's own and not something
    /// the mapper happens to supply.
    /// </summary>
    [Fact]
    public void The_constructor_default_is_no_width()
    {
        var property = typeof(PlainEntity).GetProperty(nameof(PlainEntity.Blob))!;

        new BinaryField(property, "Blob").MaxLength.Should().BeNull();
        new BinaryField(property, "Blob", maxLength: 64).MaxLength.Should().Be(64);
    }

    /// <summary>
    /// ⚠ <b>A naming pin, and it is deliberate rather than defensive.</b> The member is <c>MaxLength</c>,
    /// not <c>Lenght</c> as on <see cref="CharField"/>. That misspelling is shipped public API which cannot
    /// be renamed without breaking consumers, but a new member must not inherit it — and <c>MaxLength</c>
    /// matches <c>MaxLengthField</c> and <c>FieldDescriptor.MaxLength</c>. Asserted so a later
    /// "consistency" pass does not rename it to match its neighbour's typo.
    /// </summary>
    [Fact]
    public void The_width_member_is_spelled_MaxLength_not_Lenght()
    {
        typeof(BinaryField).GetField("MaxLength").Should().NotBeNull(
            "the correctly spelled name is the contract");
        typeof(BinaryField).GetField("Lenght").Should().BeNull(
            "CharField's typo is shipped API and stays there; it must not be propagated to new fields");
    }
}
