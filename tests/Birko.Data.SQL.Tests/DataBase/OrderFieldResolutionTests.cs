using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.SQL;
using Birko.Data.SQL.Attributes;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.DataBase;

/// <summary>
/// SH-H003 / SH-M022 (TASK-110). <c>DataBase.ResolveOrderFields</c> is the whitelist that stands between an
/// ORDER BY key and <c>CommandText</c>, which interpolates its keys verbatim.
///
/// Before it existed, <c>OrderBy&lt;T&gt;.ByName(request.Sort)</c> put caller text straight into the
/// statement. Measured on SQLite while verifying the finding:
/// <c>ByName("Rank; CREATE TABLE Pwned (x INTEGER); --")</c> CREATED that table, and
/// <c>ByName("Rank LIMIT 1 --")</c> commented out the framework's own LIMIT and returned 1 row of 3 — both
/// without raising anything. The trailing " ASC"/" DESC" the builder appends is not a mitigation; a payload
/// ending in a comment removes it.
///
/// The mechanism is resolution, NOT quoting: a key that survives this method is a name read out of table
/// metadata, so caller text can never reach the clause. Quoting was the originally filed remedy and was
/// dropped — this codebase emits every other column identifier bare (DDL, WHERE, the SELECT list), and
/// quoting only ORDER BY would break a working sort on PostgreSQL, where the unquoted DDL identifier is
/// folded to lower case.
///
/// The second half of the same call site: keys arrive as CLR property names, so a
/// <c>[NamedField("col")]</c>-remapped property was emitted under a name the table does not have. Resolution
/// fixes both at once. End-to-end proof against a real database is in
/// <c>Birko.Data.SQL.SqLite.Tests.OrderByResolutionTests</c>; these cases pin the resolver itself.
/// </summary>
public class OrderFieldResolutionTests
{
    [Table("ORows")]
    public class ORow : Models.AbstractModel
    {
        [NamedField("label_col")]
        public string? Label { get; set; }

        public int Rank { get; set; }
    }

    [Table("OSides")]
    public class OSide : Models.AbstractModel
    {
        public int Weight { get; set; }
    }

    private static IDictionary<string, bool> Keys(params string[] keys)
        => keys.ToDictionary(k => k, _ => false);

    private static IEnumerable<Birko.Data.SQL.Tables.Table> Tables(params Type[] types)
        => types.Select(global::Birko.Data.SQL.DataBase.LoadTable).ToArray();

    // ---------------------------------------------------------------- resolution

    [Fact]
    public void Property_name_resolves_to_its_column()
    {
        var resolved = global::Birko.Data.SQL.DataBase.ResolveOrderFields(Tables(typeof(ORow)), Keys("Label"));

        // The whole point of SH-M022: the CLR name "Label" must never be what reaches the clause.
        resolved!.Keys.Should().Equal("label_col");
    }

    [Fact]
    public void Unremapped_property_resolves_to_an_identical_name()
    {
        // The back-compat guarantee: a normally-named property must emit exactly what it emitted before,
        // so no consumer's working sort changes meaning.
        var resolved = global::Birko.Data.SQL.DataBase.ResolveOrderFields(Tables(typeof(ORow)), Keys("Rank"));

        resolved!.Keys.Should().Equal("Rank");
    }

    [Fact]
    public void Mapped_column_name_also_resolves()
    {
        // Passing the column name directly worked before the guard existed and has to keep working — it is
        // drawn from the same metadata, so it is equally safe.
        var resolved = global::Birko.Data.SQL.DataBase.ResolveOrderFields(Tables(typeof(ORow)), Keys("label_col"));

        resolved!.Keys.Should().Equal("label_col");
    }

    [Fact]
    public void Direction_and_key_order_survive_resolution()
    {
        // ORDER BY is positional: resolving must not reshuffle "label_col ASC, Rank DESC".
        var input = new Dictionary<string, bool> { ["Label"] = false, ["Rank"] = true };

        var resolved = global::Birko.Data.SQL.DataBase.ResolveOrderFields(Tables(typeof(ORow)), input);

        resolved!.Keys.Should().Equal("label_col", "Rank");
        resolved["label_col"].Should().BeFalse();
        resolved["Rank"].Should().BeTrue();
    }

    [Fact]
    public void Multiple_tables_qualify_the_column_with_its_table()
    {
        // A joined statement can have the same column twice, so the select name carries its prefix — the
        // rule the expression-keyed overloads used before resolution moved to one place.
        var resolved = global::Birko.Data.SQL.DataBase.ResolveOrderFields(
            Tables(typeof(ORow), typeof(OSide)), Keys("Weight"));

        resolved!.Keys.Should().Equal("OSides.Weight");
    }

    [Fact]
    public void Empty_and_null_inputs_are_passed_through()
    {
        global::Birko.Data.SQL.DataBase.ResolveOrderFields(Tables(typeof(ORow)), null).Should().BeNull();
        global::Birko.Data.SQL.DataBase.ResolveOrderFields(Tables(typeof(ORow)), new Dictionary<string, bool>())
            .Should().BeEmpty();
    }

    // ---------------------------------------------------------------- rejection

    public static IEnumerable<object[]> InjectionPayloads()
    {
        // Each of these was measured reaching CommandText before the fix; the first two had an observable
        // effect (a created table, an overridden LIMIT).
        yield return new object[] { "Rank; CREATE TABLE Pwned (x INTEGER); --" };
        yield return new object[] { "Rank LIMIT 1 --" };
        yield return new object[] { "(SELECT count(*) FROM sqlite_master)" };
        yield return new object[] { "Rank ASC, (SELECT 1)" };
        yield return new object[] { "1" };
        yield return new object[] { "Rank/**/DESC" };
        // A quoted form must not be a way back in either — the resolver compares names, it does not unquote.
        yield return new object[] { "\"Rank\"" };
    }

    [Theory]
    [MemberData(nameof(InjectionPayloads))]
    public void Injection_payloads_are_rejected(string payload)
    {
        var act = () => global::Birko.Data.SQL.DataBase.ResolveOrderFields(Tables(typeof(ORow)), Keys(payload));

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*{payload}*", "the rejection must name the offending key");
    }

    [Fact]
    public void Unknown_key_names_the_key_and_the_entity_type()
    {
        var act = () => global::Birko.Data.SQL.DataBase.ResolveOrderFields(Tables(typeof(ORow)), Keys("Nope"));

        // Naming the type is what makes the failure actionable: before this, the database answered with a
        // column name the developer never wrote ("no such column: Nope") and no indication of where from.
        act.Should().Throw<ArgumentException>()
            .WithMessage("*'Nope'*")
            .WithMessage("*ORow*");
    }

    [Fact]
    public void A_column_belonging_to_another_table_is_not_accepted()
    {
        // Resolution is scoped to the tables in the statement, not to every table the process ever loaded.
        var act = () => global::Birko.Data.SQL.DataBase.ResolveOrderFields(Tables(typeof(ORow)), Keys("Weight"));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Whitespace_key_is_rejected_rather_than_emitted()
    {
        var act = () => global::Birko.Data.SQL.DataBase.ResolveOrderFields(Tables(typeof(ORow)), Keys("   "));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Keys_with_no_table_metadata_are_rejected()
    {
        // Belt and braces: if a caller ever reaches the resolver without tables, the keys must not be
        // waved through — that would be the original defect restored by a different route.
        var act = () => global::Birko.Data.SQL.DataBase.ResolveOrderFields(
            Array.Empty<Birko.Data.SQL.Tables.Table>(), Keys("Rank"));

        act.Should().Throw<ArgumentException>();
    }
}
