using System;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests
{
    /// <summary>
    /// TASK-255 — `Birko.Data.SQL.DataBase.ValidateColumnIdentifier`, the fourth sink in the interpolated-identifier family.
    ///
    /// <para>
    /// It exists because a **column reference** must be emitted bare to resolve the case-folded column that
    /// bare-column `CREATE TABLE` actually creates on PostgreSQL, and bare removes the accidental containment
    /// that identifier quoting was providing. Its first caller is
    /// `TimescaleDBMigration.BuildContinuousAggregateSql`, whose bucketing column lands inside
    /// `time_bucket(…, &lt;col&gt;)` in a `CREATE MATERIALIZED VIEW` body — a real identifier position, so
    /// neither literal escaping nor `CatalogueNameLiteral` applies.
    /// </para>
    ///
    /// <para>
    /// **Why it is tested here rather than only from the TimescaleDB suite.** The method is declared in
    /// `Birko.Data.SQL`, and § Testing puts a project's tests in `Birko.{Project}.Tests`. TASK-257's close
    /// gate caught the same omission for `AbstractField.IsInIndexKey`, which was exercised only from the two
    /// provider suites; a guard whose only coverage lives in a consumer's suite is a guard that a consumer
    /// can delete by accident.
    /// </para>
    ///
    /// <para>
    /// **The separation from `ValidateIndexFieldIdentifier` is the message, not the check** — both share
    /// `_unqualifiedIdentifier`, deliberately, so the sinks cannot drift about what an acceptable column name
    /// is. What differs is that the index one names `CREATE INDEX` and an index column list, which would tell
    /// a migration author about indexes they never mentioned (§ Conventions, TASK-215: a refusal names the
    /// door THIS caller has). Both halves are asserted below.
    /// </para>
    /// </summary>
    public class ColumnIdentifierValidationTests
    {
        [Theory]
        [InlineData("Rank'); CREATE TABLE Pwned (x INTEGER); --")]
        [InlineData("Rank\"); CREATE TABLE Pwned (x INTEGER); --")]
        [InlineData("A, (SELECT 1)")]
        [InlineData("A B")]
        [InlineData("ts DESC")]
        public void A_column_reference_that_is_not_a_bare_identifier_is_refused(string column)
        {
            Action act = () => Birko.Data.SQL.DataBase.ValidateColumnIdentifier(column);

            act.Should().Throw<ArgumentException>(
                "the column is interpolated bare, so refusal is the only containment available — every "
              + "payload carries a space, an operator, a parenthesis or a statement separator");
        }

        /// <summary>
        /// A qualifier is refused for the same reason the index sink refuses one: this framework only ever
        /// emits a qualifier where a bare alias introduces it (TASK-211), and the statements this guards
        /// introduce none. Accepting it would turn a clear <see cref="ArgumentException"/> into a provider
        /// syntax error — the guard passing the payload's harmless cousin through to break the statement.
        /// </summary>
        [Theory]
        [InlineData("Metrics.Ts")]
        [InlineData("Docs.Status")]
        public void A_qualified_column_reference_is_refused(string column)
        {
            Action act = () => Birko.Data.SQL.DataBase.ValidateColumnIdentifier(column);

            act.Should().Throw<ArgumentException>()
                .WithMessage("*not a plain, unqualified column identifier*");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_blank_column_reference_is_refused(string? column)
        {
            Action act = () => Birko.Data.SQL.DataBase.ValidateColumnIdentifier(column);

            act.Should().Throw<ArgumentException>(
                "returning empty would emit a malformed statement where throwing reports the defect");
        }

        [Theory]
        [InlineData("Ts")]
        [InlineData("ts")]
        [InlineData("time")]
        [InlineData("_private")]
        [InlineData("Col9")]
        public void A_plain_column_reference_is_accepted_and_returned_unchanged(string column)
            => Birko.Data.SQL.DataBase.ValidateColumnIdentifier(column).Should().Be(column,
                "the guard validates; it does not fold, quote or otherwise rewrite — folding is the "
              + "parser's job wherever the name is emitted as an identifier");

        /// <summary>
        /// Anchored `\A…\z`, not `$`: in .NET `$` also matches immediately before a trailing newline, so a
        /// `$`-anchored pattern would admit a character it never listed. Same reason recorded on
        /// `_bareIdentifier`.
        /// </summary>
        [Fact]
        public void A_trailing_newline_is_refused()
        {
            Action act = () => Birko.Data.SQL.DataBase.ValidateColumnIdentifier("Ts\n");

            act.Should().Throw<ArgumentException>(
                "an anchor that admits a character the pattern never listed is the wrong anchor for a guard "
              + "whose entire job is 'these characters and no others'");
        }

        /// <summary>
        /// The two guards accept and reject exactly the same strings — one regex — and differ only in what
        /// their refusal says. Asserted so nobody "simplifies" the pair by pointing one at the other, which
        /// would restore the misleading message this method exists to avoid.
        /// </summary>
        [Theory]
        [InlineData("Status")]
        [InlineData("Docs.Status")]
        [InlineData("A B")]
        public void It_agrees_with_the_index_guard_about_what_is_acceptable(string column)
        {
            var columnThrew = Record.Exception(() => Birko.Data.SQL.DataBase.ValidateColumnIdentifier(column)) is not null;
            var indexThrew = Record.Exception(() => Birko.Data.SQL.DataBase.ValidateIndexFieldIdentifier(column)) is not null;

            columnThrew.Should().Be(indexThrew,
                "both share _unqualifiedIdentifier deliberately, so the sinks cannot drift about what an "
              + "acceptable column name is");
        }

        /// <summary>
        /// The <see cref="ArgumentException.ParamName"/> is the <i>caller's</i> parameter, not this method's.
        /// Found at TASK-255's close gate by `code-review`: the first version hardcoded `nameof(column)`, so a
        /// migration author passing a bad `timeColumn` got a `ParamName` naming a parameter that appears on
        /// neither method they called — the quiet half of the same "a refusal names the door THIS caller has"
        /// rule (§ Conventions, TASK-215) that gives this method a message separate from the index guard's.
        /// </summary>
        [Fact]
        public void Its_refusal_carries_the_callers_parameter_name()
        {
            var ex = Record.Exception(
                () => Birko.Data.SQL.DataBase.ValidateColumnIdentifier("m.ts", "timeColumn"));

            ex.Should().BeOfType<ArgumentException>()
                .Which.ParamName.Should().Be("timeColumn",
                    "a ParamName naming a parameter the caller does not have is a refusal pointing at the "
                  + "wrong door");
        }

        [Fact]
        public void Its_refusal_defaults_to_naming_the_column_when_no_caller_name_is_given()
            => Record.Exception(() => Birko.Data.SQL.DataBase.ValidateColumnIdentifier("m.ts"))
                .Should().BeOfType<ArgumentException>()
                .Which.ParamName.Should().Be("column");

        [Fact]
        public void Its_refusal_names_a_column_reference_rather_than_an_index()
        {
            var message = Record.Exception(() => Birko.Data.SQL.DataBase.ValidateColumnIdentifier("A B"))!.Message;

            message.Should().Contain("Column 'A B'")
                .And.NotContain("index",
                    "a refusal names the door THIS caller has (§ Conventions, TASK-215) — the index guard's "
                  + "wording would tell a migration author about indexes they never mentioned");
        }
    }
}
