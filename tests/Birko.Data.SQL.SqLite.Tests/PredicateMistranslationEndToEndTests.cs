using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-308 — four of the seven high <c>filter-expression-translation</c> findings, asserted as
/// <b>counted rows against a real database</b> rather than as rendered text. The rendering is pinned in
/// <c>Birko.Data.SQL.Tests.UntranslatableNodeRefusalTests</c>; here the consequence is the point, because
/// every one of these findings is a <i>silent</i> wrong answer and SQLite performs each of them without
/// complaint.
///
/// <para><b>SH-H021 / SH-H026 — an expression node the parser has no branch for.</b>
/// <c>ParseConditionExpression</c> dispatches on lambda, unary, binary, method-call and member nodes and
/// ends in <c>return Array.Empty&lt;Condition&gt;()</c>, so a <c>TypeBinaryExpression</c>
/// (<c>x.Payload is string</c>) or an <c>InvocationExpression</c> (<c>x =&gt; pred(x)</c>) produced no
/// conditions and no complaint. Measured before the fix, 3 rows seeded: at top level the read returned
/// <b>3 of 3</b>; as an <c>||</c> operand it made the whole predicate render <b>no WHERE</b> and read 3 of
/// 3, because <c>IsConstantBoolCondition</c> reads the untouched condition as constant <c>true</c>; as an
/// <c>&amp;&amp;</c> operand the term was <b>silently dropped</b>. All three now refuse.</para>
///
/// <para><b>SH-H022 — <c>ReturnSingleSubCondition</c> overwrote the enclosing negation.</b> The worst of
/// the seven, because it is the only one whose destructive path SH-H002 could not cover: the clause is
/// non-empty, it is simply <i>wrong</i>. Measured before the fix,
/// <c>x =&gt; !(x.Amount == 10 &amp;&amp; trueFlag)</c> read <b>1 row [10]</b> where 2 rows [20,30] were
/// asked for, and <c>DeleteAsync</c> with the same predicate <b>threw nothing</b> and destroyed that
/// complement.</para>
///
/// <para><b>SH-H024 — an unrecognised parameter-bound call in an UPDATE SET value</b> was invoked
/// reflectively with the entity's arguments evaluated to <c>null</c>. Measured before the fix:
/// <c>SET Name = string.Concat(r.Name, "-", r.Name)</c> stored <b><c>"-"</c></b>.</para>
/// </summary>
public class PredicateMistranslationEndToEndTests : IDisposable
{
    private readonly string _root;
    private SqLiteSettings _settings = null!;

    public PredicateMistranslationEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-mistranslation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Row : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }

        /// <summary>
        /// Unmapped on purpose: it exists only to give the test a <c>TypeBinaryExpression</c>
        /// (<c>x.Payload is string</c>). An <c>object</c> property has no SQL column mapping and
        /// § SH-H037 makes that a load-time throw, so it must be excluded deliberately.
        /// </summary>
        [Birko.Data.SQL.Attributes.IgnoreField]
        public object? Payload { get; set; }
    }

    private sealed class RowMapping : IModelMapping<Row>
    {
        public void Configure(ModelMap<Row> map)
        {
            map.ToTable("MistranslationRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
        }
    }

    private async Task<AsyncSQLiteStore<Row>> Seeded()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new RowMapping());
        registry.ApplyToDatabase();

        var store = new AsyncSQLiteStore<Row>();
        _settings = new SqLiteSettings(_root, $"m{Guid.NewGuid():N}.db");
        store.SetSettings(_settings);
        for (var i = 1; i <= 3; i++)
        {
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = $"r{i}", Amount = i * 10 });
        }

        (await store.ReadAsync(CancellationToken.None)).Should().HaveCount(3, "seed");
        return store;
    }

    private static async Task<List<Row>> Rows(AsyncSQLiteStore<Row> store)
        => (await store.ReadAsync(CancellationToken.None)).ToList();

    private static async Task<List<Row>> Rows(AsyncSQLiteStore<Row> store, Expression<Func<Row, bool>> f)
        => (await store.ReadAsync(f, null, null, null, CancellationToken.None)).ToList();

    // ── SH-H021 / SH-H026: the READ is the half that was unguarded ──────────────────────────────────

    [Fact]
    public async Task An_untranslatable_node_at_top_level_is_refused_instead_of_reading_every_row()
    {
        var store = await Seeded();

        var act = async () => await Rows(store, x => x.Payload is string);

        await act.Should().ThrowAsync<NotSupportedException>();
        (await Rows(store)).Should().HaveCount(3, "the read is refused, not answered");
    }

    [Fact]
    public async Task An_untranslatable_OR_operand_is_refused_instead_of_dropping_the_whole_WHERE()
    {
        // The SH-H021 mechanism: the unhandled operand leaves a condition that IsConstantBoolCondition
        // reads as constant TRUE, and `TRUE || anything` renders no WHERE at all. Measured before: 3 of 3.
        var store = await Seeded();

        var act = async () => await Rows(store, x => (x.Payload is string) || x.Amount == 10);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task An_untranslatable_AND_operand_is_refused_instead_of_being_silently_dropped()
    {
        // Measured before the fix this returned 1 row [10] — the right COUNT for the wrong reason, since
        // the `is string` conjunct was never applied to it. A row count alone cannot catch that, which is
        // why the refusal is what is asserted.
        var store = await Seeded();

        var act = async () => await Rows(store, x => (x.Payload is string) && x.Amount == 10);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task An_invocation_expression_is_refused_on_a_read()
    {
        var store = await Seeded();
        Func<Row, bool> pred = r => r.Amount > 10;

        var act = async () => await Rows(store, x => pred(x));

        await act.Should().ThrowAsync<NotSupportedException>();
        (await Rows(store)).Should().HaveCount(3);
    }

    // ── SH-H022: the complement, on a read and on a delete ─────────────────────────────────────────

    [Fact]
    public async Task A_negated_group_whose_other_operand_is_a_constant_keeps_its_negation_on_a_read()
    {
        var store = await Seeded();
        var trueFlag = true;

        var rows = await Rows(store, x => !(x.Amount == 10 && trueFlag));

        rows.Select(r => r.Amount).Should().BeEquivalentTo(new[] { 20, 30 },
            "before the fix the negation was overwritten and this returned the complement, [10]");
    }

    [Fact]
    public async Task A_negated_group_whose_other_operand_is_a_constant_deletes_the_rows_it_names()
    {
        // ⚠ This is the assertion that matters most in the file. The clause is non-empty, so SH-H002's
        // AddRequiredWhere had nothing to refuse: measured before the fix, this call threw nothing and left
        // [20,30] — it destroyed the complement of what was asked for, silently.
        var store = await Seeded();
        var trueFlag = true;

        await store.DeleteAsync(x => !(x.Amount == 10 && trueFlag), CancellationToken.None);

        (await Rows(store)).Select(r => r.Amount).Should().BeEquivalentTo(new[] { 10 },
            "the predicate names rows 20 and 30, so those are the rows that go");
    }

    [Fact]
    public async Task A_doubly_negated_group_cancels_rather_than_negating_twice()
    {
        // XOR, not OR: `!(x.Amount != 10 && trueFlag)` is `x.Amount == 10`. A fix that combined the two
        // negations with `||` would return the complement here, so this is what separates the two spellings.
        var store = await Seeded();
        var trueFlag = true;

        var rows = await Rows(store, x => !(x.Amount != 10 && trueFlag));

        rows.Select(r => r.Amount).Should().BeEquivalentTo(new[] { 10 });
    }

    [Fact]
    public async Task A_plain_negation_is_unchanged()
    {
        // Contract pin: `!(x.Amount == 10)` never went through ReturnSingleSubCondition, so it was correct
        // before and must stay correct. It is the control the defect was measured against.
        var store = await Seeded();

        var rows = await Rows(store, x => !(x.Amount == 10));

        rows.Select(r => r.Amount).Should().BeEquivalentTo(new[] { 20, 30 });
    }

    // ── the doors that must stay open ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_operand_that_genuinely_reduces_to_every_row_still_means_every_row()
    {
        // ⚠ The reason the fix is a guard on the NODE rather than an edit to IsConstantBoolCondition.
        // `(x.Amount == 10 || true)` legitimately leaves the same "nothing was parsed" state that an
        // unhandled node leaves, and `true` is the correct reading there — so the two cases had to be told
        // apart, not merged. Here the left conjunct is all-rows, leaving `x.Amount >= 20`.
        var store = await Seeded();

        var rows = await Rows(store, x => (x.Amount == 10 || true) && x.Amount >= 20);

        rows.Select(r => r.Amount).Should().BeEquivalentTo(new[] { 20, 30 });
    }

    [Fact]
    public async Task An_ordinary_predicate_and_the_explicit_all_rows_synonym_both_still_work()
    {
        var store = await Seeded();

        (await Rows(store, x => x.Amount == 10)).Should().HaveCount(1);
        (await Rows(store, x => x.Amount > 10)).Should().HaveCount(2);
        (await Rows(store, x => true)).Should().HaveCount(3, "the documented all-rows synonym");
    }

    [Fact]
    public async Task A_translated_method_call_still_works()
    {
        // Guards against the node whitelist being read as a method-call whitelist: `Contains` and the
        // string methods are MethodCallExpressions, which the parser does claim.
        var store = await Seeded();
        var names = new List<string> { "r1", "r3" };

        (await Rows(store, x => names.Contains(x.Name!))).Should().HaveCount(2);
        (await Rows(store, x => x.Name!.StartsWith("r"))).Should().HaveCount(3);
    }

    [Fact]
    public async Task The_null_filter_refusal_on_a_destructive_write_is_unchanged()
    {
        // Contract pin, not evidence: SH-H002's guard still owns the causes that reach it. Only the
        // untranslatable cause moved upstream.
        var store = await Seeded();

        var act = async () => await store.DeleteAsync((Expression<Func<Row, bool>>)null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
        (await Rows(store)).Should().HaveCount(3);
    }

    // ── SH-H024: the UPDATE SET value ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unrecognised_parameter_bound_call_in_a_SET_value_is_refused_not_invoked()
    {
        var store = await Seeded();
        var connector = SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        var sets = new Dictionary<Expression<Func<Row, string?>>, Expression<Func<Row, string?>>>
        {
            // string.Concat is not one of the translated calls (Replace / ToLower / ToUpper).
            { r => r.Name, r => string.Concat(r.Name, "-", r.Name) },
        };

        var act = () => connector.Update(
            typeof(Row), sets,
            SQL.DataBase.ParseConditionExpression((Expression<Func<Row, bool>>)(x => x.Amount == 10)));

        act.Should().Throw<NotSupportedException>().WithMessage("*cannot be translated to SQL*");

        var rows = await Rows(store);
        rows.Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1", "r2", "r3" },
            "before the fix the 10-row's Name became \"-\" — Concat(null, \"-\", null) — with no exception");
    }

    [Fact]
    public async Task A_translated_call_in_a_SET_value_still_works()
    {
        // § SH-H037's opt-out, executed rather than reasoned about: the refusal above must not have closed
        // the door on the calls this path does translate.
        //
        // ⚠ `Replace` rather than `ToUpper`, and the difference is not cosmetic — see
        // An_expression_SET_that_binds_no_parameter_is_a_SILENT_NO_OP below. `ToUpper()` binds no constant,
        // so the whole UPDATE is skipped by a pre-existing gate and this test would have passed for a
        // reason that has nothing to do with the guard it is meant to exercise.
        var store = await Seeded();
        var connector = SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        var sets = new Dictionary<Expression<Func<Row, string?>>, Expression<Func<Row, string?>>>
        {
            { r => r.Name, r => r.Name!.Replace("r", "R") },
        };

        connector.Update(
            typeof(Row), sets,
            SQL.DataBase.ParseConditionExpression((Expression<Func<Row, bool>>)(x => x.Amount == 10)));

        var rows = await Rows(store);
        rows.Single(r => r.Amount == 10).Name.Should().Be("R1");
        rows.Where(r => r.Amount != 10).Select(r => r.Name).Should().BeEquivalentTo(new[] { "r2", "r3" });
    }

    /// <summary>
    /// ⚠ <b>Pins a defect this task did NOT fix, so it cannot be believed fixed.</b> Found 2026-09-09 while
    /// writing the opt-out test above, and owned by [[TASK-331]] rather than folded into TASK-308 — it is a
    /// different root cause (an eligibility gate, not a translation) and is out of this task's scope.
    /// <para><c>AbstractConnector_Update.Update(tableName, fields, values, conditions, isExpressionValues)</c>
    /// opens with <c>if (values != null &amp;&amp; values.Any())</c>. On the <b>expression</b>-valued path
    /// <c>values</c> holds only the constants the SET expressions bound, so a SET that binds none —
    /// <c>r =&gt; r.Name.ToUpper()</c>, <c>r =&gt; r.A + r.B</c>, any pure column expression — makes the
    /// method return having issued nothing. No statement, no exception, no log entry.</para>
    /// <para>Assert the defect, not the remedy: when TASK-331 lands this test fails, and that failure is
    /// the instruction to invert it.</para>
    /// </summary>
    [Fact]
    public async Task An_expression_SET_that_binds_no_parameter_is_a_SILENT_NO_OP()
    {
        var store = await Seeded();
        var connector = SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        var sets = new Dictionary<Expression<Func<Row, string?>>, Expression<Func<Row, string?>>>
        {
            { r => r.Name, r => r.Name!.ToUpper() },
        };

        connector.Update(
            typeof(Row), sets,
            SQL.DataBase.ParseConditionExpression((Expression<Func<Row, bool>>)(x => x.Amount == 10)));

        (await Rows(store)).Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1", "r2", "r3" },
            "UPPER(Name) binds no parameter, so values is empty and the UPDATE is skipped entirely — "
            + "the row that should read R1 still reads r1, with nothing reported. TASK-331 owns this.");
    }

    [Fact]
    public async Task A_parameter_free_call_in_a_SET_value_is_still_folded_to_a_constant()
    {
        // The other half of the SH-H024 guard: it keys on ContainsParameter, so a call that does not
        // reference the entity is still evaluated and bound. Without this the guard would be a blanket
        // refusal of every method call in a SET value.
        var store = await Seeded();
        var connector = SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        var suffix = "X";
        var sets = new Dictionary<Expression<Func<Row, string?>>, Expression<Func<Row, string?>>>
        {
            { r => r.Name, r => string.Concat("fixed", suffix) },
        };

        connector.Update(
            typeof(Row), sets,
            SQL.DataBase.ParseConditionExpression((Expression<Func<Row, bool>>)(x => x.Amount == 10)));

        (await Rows(store)).Single(r => r.Amount == 10).Name.Should().Be("fixedX");
    }
}
