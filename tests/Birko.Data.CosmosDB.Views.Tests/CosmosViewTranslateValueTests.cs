using System;
using System.Linq.Expressions;
using Birko.Data.CosmosDB.Views;
using FluentAssertions;
using Xunit;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// CR-M086, <b>inverted by TASK-447</b>: what these assertions are about has moved a layer.
/// </summary>
/// <remarks>
/// <para>
/// CR-M086 fixed a rendering defect — <c>TranslateValue</c> quoted only string and Guid and fell back
/// to a raw <c>ToString()</c>, so a <c>DateTime</c> filter produced a culture-dependent, unquoted,
/// invalid SQL literal and an enum emitted its member name. The fix was a hand-written literal
/// formatter, and this file pinned it.
/// </para>
/// <para>
/// TASK-447 removed that formatter: values are now <b>bound as query parameters</b> rather than
/// rendered into the statement, because escaping the literal grammar was the injection sink (a value
/// containing a backslash broke out of its own quotes). So there is no literal to assert. These tests
/// are <b>inverted, not deleted</b> — each one still covers its original type, now asserting the CLR
/// value that reaches the parameter (CLAUDE.md § TASK-211: a narrowing breaks the tests that asserted
/// the wide behaviour, and those are the interesting ones).
/// </para>
/// <para>
/// ⚠ <b>What this file can no longer prove, stated rather than quietly dropped.</b> The wire format of
/// a bound <c>DateTime</c>/<c>Guid</c> is now the SDK serializer's, and no offline test can observe
/// it. That is the deliberate trade: the same serializer writes the documents, so a parameter matches
/// the stored form by construction instead of by a hand-rolled guess about System.Text.Json's
/// defaults — which is what CR-M086 was doing, twice. The one formatting decision kept in code is the
/// enum, and it is asserted below with its reason.
/// </para>
/// </remarks>
public class CosmosViewTranslateValueTests
{
    private enum Status
    {
        Draft = 0,
        Published = 2,
    }

    private class V
    {
        public string? Name { get; set; }
        public DateTime When { get; set; }
        public DateTimeOffset WhenOffset { get; set; }
        public Status State { get; set; }
        public Guid Id { get; set; }
        public int Count { get; set; }
        public bool Flag { get; set; }
        public object? Boxed { get; set; }
    }

    private static object? Evaluate(object? value, Type? type = null)
    {
        var expr = value is null && type != null
            ? Expression.Constant(null, type)
            : Expression.Constant(value);
        return CosmosFilterTranslator.EvaluateValue(expr);
    }

    /// <summary>Renders a filter and returns the single value it bound.</summary>
    private static object? BoundValue(Expression<Func<V, bool>> filter)
    {
        var parameters = new System.Collections.Generic.List<
            System.Collections.Generic.KeyValuePair<string, object?>>();
        CosmosFilterTranslator.Translate(filter, parameters);
        parameters.Should().ContainSingle("each of these filters compares against exactly one value");
        return parameters[0].Value;
    }

    [Fact]
    public void DateTime_reaches_the_parameter_as_a_DateTime()
    {
        var dt = new DateTime(2026, 6, 17, 8, 30, 0, DateTimeKind.Utc);

        Evaluate(dt).Should().Be(dt);
        BoundValue(v => v.When == dt).Should().Be(dt, "the SDK serializes it, so the CLR value is what must arrive intact");
    }

    [Fact]
    public void DateTimeOffset_reaches_the_parameter_as_a_DateTimeOffset()
    {
        var dto = new DateTimeOffset(2026, 6, 17, 8, 30, 0, TimeSpan.Zero);

        Evaluate(dto).Should().Be(dto);
        BoundValue(v => v.WhenOffset == dto).Should().Be(dto);
    }

    [Fact]
    public void An_ordinary_enum_comparison_binds_the_UNDERLYING_int_because_C_sharp_pre_converts_it()
    {
        // ⚠ Measured, and it corrected this test. C# builds an enum `==` as
        // `Convert(v.State, Int32) == Convert(2, Int32)`, so the operand is a UnaryExpression that
        // evaluates to a boxed **Int32** and never reaches the `is Enum` branch below. CR-M086's
        // numeric-form requirement is therefore satisfied by the compiler here, not by our code.
        BoundValue(v => v.State == Status.Published).Should().Be(2);
    }

    [Fact]
    public void A_BOXED_enum_operand_is_converted_to_its_numeric_value()
    {
        // This is the shape that actually reaches the `is Enum` branch: an operand whose static type
        // is `object`, so the compiler inserts no conversion and the enum arrives intact.
        //
        // ⚠ The first version of the test above used the ordinary shape and therefore passed with the
        // branch deleted - a mutation that fails nothing is a missing test (CLAUDE.md § TASK-261).
        // Keeping the branch matters because Birko's own Cosmos store writes enums in numeric form,
        // and the SDK would serialize a bare enum through its own converter.
        BoundValue(v => v.Boxed == (object)Status.Published).Should().Be(2L);
    }

    [Fact]
    public void A_string_reaches_the_parameter_VERBATIM_with_no_escaping()
    {
        // The whole point of TASK-447: the value is data, not text spliced into a statement, so
        // nothing is escaped and nothing needs to be. `O'Brien` used to render as `'O\'Brien'`.
        BoundValue(v => v.Name == "O'Brien").Should().Be("O'Brien");
    }

    [Fact]
    public void Bool_number_guid_and_null_reach_the_parameter_unchanged()
    {
        var g = Guid.NewGuid();

        BoundValue(v => v.Flag == true).Should().Be(true);
        BoundValue(v => v.Count == 42).Should().Be(42);
        BoundValue(v => v.Id == g).Should().Be(g);
        BoundValue(v => v.Name == null).Should().BeNull();
        Evaluate(null, typeof(string)).Should().BeNull();
    }
}
