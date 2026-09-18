using System;
using Birko.Data.Exceptions;
using Birko.Data.Migrations.Context;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.Tests;

/// <summary>
/// <b>SH-H032 (TASK-314).</b> The shared producer for "does this migration filter constrain anything?".
///
/// <para>
/// It lives in <c>Birko.Data.Migrations</c> — the base every migrator already imports — rather than four
/// times in four provider repos, which is the layer lesson § TASK-329 records: a rule with one statement
/// and four implementations is one that will be got wrong again. These tests own the rule itself; each
/// provider suite then asserts that its own translator is wired to it.
/// </para>
/// </summary>
public class MigrationFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("  {}  ")]
    public void No_filter_at_all_is_the_deliberate_match_all_door(string? filterJson)
    {
        MigrationFilter.IsExplicitMatchAll(filterJson).Should().BeTrue();

        // …and therefore an empty translation of it is allowed through.
        Action act = () => MigrationFilter.RequireBounded(filterJson, false, "delete", "T", "everything");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("{\"status\":{}}")]
    [InlineData("{\"a\":\"x\"}")]
    public void A_filter_that_was_supplied_is_not_the_door(string filterJson)
    {
        MigrationFilter.IsExplicitMatchAll(filterJson).Should().BeFalse();
    }

    [Fact]
    public void A_supplied_filter_that_constrains_nothing_is_refused()
    {
        Action act = () => MigrationFilter.RequireBounded(
            "{\"status\":{}}", translationConstrains: false, "delete", "Widgets", "every row in the table");

        var ex = act.Should().Throw<WholeTableWriteException>().Which;
        ex.Operation.Should().Be("delete");
        ex.TableName.Should().Be("Widgets");
        ex.Message.Should().Contain("every row in the table");
    }

    /// <summary>
    /// The refusal must be selectable by the same <c>catch</c> as every other whole-table refusal in the
    /// framework — § Conventions: <i>do not invent a per-backend exception</i>.
    /// </summary>
    [Fact]
    public void The_refusal_is_the_frameworks_own_whole_table_type()
    {
        Action act = () => MigrationFilter.RequireBounded("{\"a\":{}}", false, "update", "T", "everything");

        act.Should().Throw<WholeTableWriteException>()
            .And.Should().BeAssignableTo<InvalidOperationException>();
    }

    /// <summary>
    /// A JSON-filter caller holds no expression tree, so naming <c>x =&gt; true</c> would point at a door
    /// that does not exist here. That is the defect <see cref="WholeTableWriteException"/>'s own remarks
    /// warn about, which is why this overload exists at all rather than reusing the predicate wording.
    /// </summary>
    [Fact]
    public void The_refusal_names_only_a_door_a_json_caller_has()
    {
        Action act = () => MigrationFilter.RequireBounded("{\"a\":{}}", false, "delete", "T", "everything");

        var message = act.Should().Throw<WholeTableWriteException>().Which.Message;

        message.Should().Contain("{}");
        message.Should().NotContain("x => true");
        message.Should().NotContain("DeleteAll");
        message.Should().NotContain("Destroy()");
    }

    // ---------------------------------------------------------------- contract pins

    [Fact]
    public void A_filter_that_constrains_is_always_allowed()
    {
        Action act = () => MigrationFilter.RequireBounded(
            "{\"status\":\"active\"}", translationConstrains: true, "delete", "T", "everything");

        act.Should().NotThrow();
    }

    /// <summary>
    /// Guard on the RENDERED result, not on a re-parse. If a backend reports it produced terms, the guard
    /// defers to it even for input this class would otherwise consider empty — so the guard and the
    /// statement that was actually built cannot disagree (§ TASK-137).
    /// </summary>
    [Fact]
    public void The_backends_own_translation_is_what_decides()
    {
        Action act = () => MigrationFilter.RequireBounded("{\"status\":{}}", true, "delete", "T", "everything");

        act.Should().NotThrow();
    }
}
