using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// SH-H002 / SH-M023 (TASK-109) — a null or untranslatable filter reached the connector and rendered
/// `DELETE FROM "T"` / `UPDATE "T" SET …` with **no WHERE clause**, i.e. the whole table.
///
/// <para>These tests work at the rendering layer, which is where the defect manifests: the guard is on the
/// *rendered* WHERE string rather than on the condition collection, because
/// <c>ConditionDefinition</c> returns <c>string.Empty</c> for a null OR empty enumerable and builds from
/// <c>BuildSingleCondition</c>, which can yield <c>""</c> for a malformed condition — so a non-empty
/// collection can still produce no WHERE.</para>
/// </summary>
public class DestructiveFilterGuardTests
{
    private class Widget : AbstractModel
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }

    /// <summary>Minimal concrete connector to reach the base's rendering members (the base is abstract).</summary>
    private sealed class FakeConnector : Birko.Data.SQL.Connectors.AbstractConnectorBase
    {
        public FakeConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public override DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(DbType type, Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
    }

    private static string Render(Expression<Func<Widget, bool>>? filter)
    {
        var conditions = global::Birko.Data.SQL.DataBase.ParseConditionExpression(filter);
        return new FakeConnector().ConditionDefinition(conditions, new TestDbCommand());
    }

    // ---- criterion 4: a predicate that legitimately matches NOTHING must still emit a WHERE ----
    //
    // Verified BEFORE writing any guard, because if this already holds the criterion is a contract pin
    // rather than a change. `_ => false` is handled explicitly by the parser (DataBase.cs:334-339 via
    // MakeFalseCondition:927), and an empty IN renders `1 = 0` (InConditionStrategy.cs:33).

    [Fact]
    public void MatchesNothing_StillRendersAWhereClause()
    {
        Render(x => false).Should().NotBeEmpty(
            "a predicate that matches nothing is a legitimate translation — it must render an always-false "
                + "WHERE, never be omitted, or a DELETE would affect every row instead of none");
    }

    // The empty-IN half of criterion 4 is already pinned one layer down, at the strategy that renders it:
    // Strategies/InConditionStrategyTests.cs:121,136 assert `1 = 0` for an empty IN and `1 = 1` for an empty
    // NOT IN. Not duplicated here — asserting it through the whole parse+render path would need an
    // IModelMapping registration purely to resolve a column name that the constant rendering never uses.
    // (That `1 = 1` for the empty NOT IN is TASK-137's subject, not this task's.)

    // ---- the four legitimate "means everything" shapes render NOTHING ----
    //
    // This is the finding that made a parser-level "empty means untranslatable" inference unsound, and it is
    // pinned here so the assumption cannot be quietly reintroduced. All four are CORRECT translations of
    // "every row" — the defect was never that they render nothing, it is that a *destructive* statement then
    // carries no WHERE.

    [Fact]
    public void ConstantTruePredicate_RendersNoWhere()
        => Render(x => true).Should().BeEmpty();

    [Fact]
    public void ParameterFreeTrueBinary_RendersNoWhere()
        => Render(x => 1 == 1).Should().BeEmpty();

    [Fact]
    public void OrWithConstantTrueSide_RendersNoWhere()
        => Render(x => true || x.Count > 5).Should().BeEmpty();

    [Fact]
    public void NullFilter_RendersNoWhere()
        => Render(null).Should().BeEmpty(
            "a null filter is read-everything — a documented API on reads, and the thing that must be "
                + "refused on the destructive paths");

    // ---- an untranslatable predicate is indistinguishable from the above at this layer ----

    [Fact]
    public void UntranslatablePredicate_RendersNoWhere_SoItCannotBeToldApartFromMeaningEverything()
    {
        Func<Widget, bool> pred = w => w.Count > 5;
        // An InvocationExpression: the parser has no branch for it, so it falls through to the same empty
        // result as `x => true`. THIS is why the guard is on the rendered WHERE and the causes are not
        // classified — at the point of decision they are the same observation.
        Render(x => pred(x)).Should().BeEmpty();
    }

    // ---- the guard itself: AddRequiredWhere ----

    private static DbCommand Destructive(Expression<Func<Widget, bool>>? filter, bool allowAllRows = false)
    {
        var command = new TestDbCommand { CommandText = "DELETE FROM \"Widgets\"" };
        var conditions = global::Birko.Data.SQL.DataBase.ParseConditionExpression(filter);
        new FakeConnector().AddRequiredWhere(conditions, command, "delete", "Widgets", allowAllRows);
        return command;
    }

    [Fact]
    public void AddRequiredWhere_Throws_WhenNothingWouldBeRendered()
    {
        Func<Widget, bool> pred = w => w.Count > 5;

        var act = () => Destructive(x => pred(x));

        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>()
            .Which.Should().Match<Birko.Data.Exceptions.WholeTableWriteException>(
                e => e.Operation == "delete" && e.TableName == "Widgets");
    }

    [Fact]
    public void AddRequiredWhere_Throws_OnANullFilter()
        => ((Action)(() => Destructive(null))).Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>();

    [Fact]
    public void AddRequiredWhere_DerivesFromInvalidOperationException_SoExistingCatchesKeepWorking()
        => ((Action)(() => Destructive(null))).Should().Throw<InvalidOperationException>();

    [Fact]
    public void AddRequiredWhere_ErrorNamesTheExplicitAlternative()
    {
        var act = () => Destructive(null);

        // The message has to tell the caller how to do what they meant, or the guard just blocks people.
        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>()
            .WithMessage("*DeleteAll()*").WithMessage("*Destroy()*");
    }

    // "A translating filter still gets its WHERE" needs a column name, which needs an IModelMapping
    // registration — so it is asserted end-to-end in Birko.Data.SQL.SqLite.Tests
    // (DestructiveFilterEndToEndTests), where the whole parse→render→execute path is real and the row set
    // afterwards is the actual proof. The always-false case below covers the rendering half here.

    [Fact]
    public void AddRequiredWhere_StillAppendsAnAlwaysFalseWhere_SoMatchesNothingDeletesNothing()
    {
        // The distinction this task exists to preserve: "matches nothing" must NOT become "matches
        // everything" by having its WHERE omitted.
        var command = Destructive(x => false);

        command.CommandText.Should().Contain(" WHERE ");
        command.CommandText.Should().NotBe("DELETE FROM \"Widgets\"");
    }

    // ---- the explicit all-rows door: clean SQL, no marker ----

    [Fact]
    public void AllRowsOptIn_EmitsACleanConditionlessStatement()
    {
        var command = Destructive(null, allowAllRows: true);

        command.CommandText.Should().Be("DELETE FROM \"Widgets\"",
            "a deliberate all-rows delete is spelled by the CALL SITE, not by a marker in the SQL");
    }

    [Fact]
    public void AllRowsOptIn_DoesNotEmitAnInjectionLookalike()
    {
        // `1 = 1` is the signature of `' OR 1=1--`. Emitting it during normal operation would train
        // operators to scroll past the pattern they are supposed to react to, so the all-rows form must
        // stay clean. Guards the decision, not just the implementation.
        Destructive(null, allowAllRows: true).CommandText.Should().NotContain("1 = 1");
        Destructive(x => true, allowAllRows: true).CommandText.Should().NotContain("1 = 1");
    }

    // ---- IsExplicitAllRows: one node type after normalization, not a shape whitelist ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsExplicitAllRows_RecognisesAConstantBody(bool value)
    {
        Expression<Func<Widget, bool>> filter = value ? (x => true) : (x => false);

        global::Birko.Data.SQL.DataBase.IsExplicitAllRows(filter).Should().Be(value);
    }

    [Fact]
    public void IsExplicitAllRows_RecognisesAParameterFreeTrueBinary_ViaNormalization()
        => global::Birko.Data.SQL.DataBase.IsExplicitAllRows((Expression<Func<Widget, bool>>)(x => 1 == 1))
            .Should().BeTrue("ExpressionNormalizer funcletizes it to the same ConstantExpression as `x => true`");

    [Fact]
    public void IsExplicitAllRows_RecognisesACapturedFlag_ViaNormalization()
    {
        var flag = true;
        global::Birko.Data.SQL.DataBase.IsExplicitAllRows((Expression<Func<Widget, bool>>)(x => flag))
            .Should().BeTrue();
    }

    [Fact]
    public void IsExplicitAllRows_IsFalseForARealPredicate()
        => global::Birko.Data.SQL.DataBase.IsExplicitAllRows((Expression<Func<Widget, bool>>)(x => x.Count > 5))
            .Should().BeFalse();

    [Fact]
    public void IsExplicitAllRows_IsFalseForAnOrWithAConstantTrueSide()
        => global::Birko.Data.SQL.DataBase.IsExplicitAllRows((Expression<Func<Widget, bool>>)(x => true || x.Count > 5))
            .Should().BeFalse(
                "the parser reduces this to 'everything', but it is NOT the explicit door — it is refused, "
                    + "with a message naming DeleteAll(). Recognising it would mean whitelisting shapes, which "
                    + "rots the moment the parser gains a fifth reduce-to-everything path");

    [Fact]
    public void IsExplicitAllRows_IsFalseForNull()
        => global::Birko.Data.SQL.DataBase.IsExplicitAllRows(null).Should().BeFalse();
}
