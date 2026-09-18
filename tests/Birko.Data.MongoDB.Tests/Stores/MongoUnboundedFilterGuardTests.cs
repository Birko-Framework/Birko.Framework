using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.MongoDB.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-212 — a filter that is PRESENT but constrains nothing reached <c>DeleteMany</c> unrefused.
///
/// <para><b>The mechanism.</b> The four MongoDB overrides called <c>RequireFilter</c>, which refuses only a
/// <b>null</b> filter, then handed the expression straight to the driver. Measured offline against
/// MongoDB.Driver 3.2.0 (rendering a <c>FilterDefinition</c> needs no connection):
/// <c>x =&gt; !empty.Contains(x.Amount)</c> renders <c>{ "Amount" : { "$nin" : [] } }</c> — a
/// <b>one-element</b> document, indistinguishable by inspection from an ordinary field predicate, while
/// <c>$nin</c> over an empty array excludes nothing and so selects every document.</para>
///
/// <para><b>Why the guard is on the expression.</b> Because of that rendering, a guard asking "is the emitted
/// filter empty?" would not fire — the same trap as the SQL side, where <c>1 = 1</c> was a non-empty
/// <c>WHERE</c> and satisfied a guard testing whether anything had been rendered (TASK-137). The C#
/// expression is unambiguous where the translation is not: <c>!empty.Contains(x.Amount)</c> is true of every
/// entity by C# semantics, so the refusal does not depend on how any driver evaluates <c>$nin: []</c>.</para>
///
/// <para><b>Offline by construction.</b> Both guards throw before the <c>Collection == null</c> check and
/// before any driver call, and the parameterless constructors leave <c>Client</c> null — so nothing here
/// opens a socket. Deliberate: the regression suite for a destructive-write guard must not itself be
/// destructive, and a slow suite is evidence it is not offline (TASK-117).</para>
/// </summary>
public class MongoUnboundedFilterGuardTests
{
    public class MgScopeModel : MongoDBModel
    {
        public string? Label { get; set; }
        public int Amount { get; set; }
    }

    private static MongoDBStore<MgScopeModel> SyncStore() => new();
    private static AsyncMongoDBStore<MgScopeModel> AsyncStore() => new();

    private static PropertyUpdate<MgScopeModel> Updates()
        => new PropertyUpdate<MgScopeModel>().Set(x => x.Label, "changed");

    private static readonly List<int> Empty = new();
    private static readonly List<int> Some = new() { 1, 5 };

    // ── the defect: a present filter that covers everything is refused on all four overrides ─────────────

    [Fact]
    public void Delete_with_an_empty_negated_Contains_is_refused()
    {
        var act = () => SyncStore().Delete(x => !Empty.Contains(x.Amount));

        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>()
            .Which.Operation.Should().Be("delete");
    }

    [Fact]
    public void Update_with_an_empty_negated_Contains_is_refused()
    {
        var act = () => SyncStore().Update(x => !Empty.Contains(x.Amount), Updates());

        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>()
            .Which.Operation.Should().Be("update");
    }

    [Fact]
    public async Task DeleteAsync_with_an_empty_negated_Contains_is_refused()
    {
        var act = async () => await AsyncStore().DeleteAsync(x => !Empty.Contains(x.Amount));

        await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    [Fact]
    public async Task UpdateAsync_with_an_empty_negated_Contains_is_refused()
    {
        var act = async () => await AsyncStore().UpdateAsync(x => !Empty.Contains(x.Amount), Updates());

        await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    [Fact]
    public void An_OR_chain_containing_an_unbounded_term_is_refused()
    {
        // `A || TRUE` is TRUE. The driver renders this as a $or whose second branch is `$nin: []`, so the
        // whole filter still selects every document.
        var act = () => SyncStore().Delete(x => x.Amount > 4 || !Empty.Contains(x.Amount));

        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    // ── the refusal is catchable the same way on every backend, and names the door ───────────────────────

    [Fact]
    public void The_refusal_is_the_same_type_the_SQL_connectors_throw()
    {
        // One `catch` has to select the refusal whatever the backend — which is why the exception now lives
        // in Birko.Data.Core beside StoreException rather than in Birko.Data.SQL.
        var act = () => SyncStore().Delete(x => !Empty.Contains(x.Amount));

        act.Should().Throw<InvalidOperationException>("existing catch blocks must keep working");
        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    [Fact]
    public void The_refusal_names_the_deliberate_door_and_not_a_SQL_one()
    {
        var act = () => SyncStore().Delete(x => !Empty.Contains(x.Amount));

        var message = act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>().Which.Message;

        message.Should().Contain("DeleteAll()", "a guard that only says no gets reached around");
        message.Should().NotContain("WHERE", "a document store has no WHERE clause to point at");
        message.Should().NotContain("Destroy()", "and no Destroy() either — that would send the reader "
            + "looking for an API this store does not have");
    }

    // ── the doors that must stay open: the guard must not become a wall ──────────────────────────────────

    [Fact]
    public void An_explicit_constant_true_filter_is_NOT_refused()
    {
        // `x => true` is the documented DeleteAll() synonym. It reaches the driver, renders `{ }`, and is
        // checked BEFORE the scope test so the guard has a door (§ SH-H037). With no Client the call is a
        // no-op, which is exactly why this can be asserted offline.
        var act = () => SyncStore().Delete(x => true);

        act.Should().NotThrow<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    [Fact]
    public void A_captured_true_flag_is_NOT_refused_either()
    {
        // Normalization folds `x => flag` to the same single ConstantExpression as `x => true`, so the
        // synonym survives the indirection a caller is likely to write.
        var flag = true;

        var act = () => SyncStore().Delete(x => flag);

        act.Should().NotThrow<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    [Fact]
    public void A_bounded_filter_is_NOT_refused()
    {
        var act = () => SyncStore().Delete(x => x.Amount > 4);

        act.Should().NotThrow<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    [Fact]
    public void A_NON_empty_negated_Contains_is_NOT_refused()
    {
        // The refusal must fire on "everything", never on "a set I happen to dislike". `$nin: [1,5]`
        // genuinely constrains.
        var act = () => SyncStore().Delete(x => !Some.Contains(x.Amount));

        act.Should().NotThrow<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    [Fact]
    public void An_empty_UN_negated_Contains_is_NOT_refused()
    {
        // `$in: []` matches NOTHING. A delete that affects no document is harmless, and refusing it would be
        // a new defect — the inverse mistake to the one being fixed.
        var act = () => SyncStore().Delete(x => Empty.Contains(x.Amount));

        act.Should().NotThrow<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    [Fact]
    public void An_AND_chain_with_one_bounded_term_is_NOT_refused()
    {
        // `A && TRUE` is `A`, which constrains. Only a chain where EVERY term is unbounded covers everything.
        var act = () => SyncStore().Delete(x => x.Amount > 4 && !Empty.Contains(x.Amount));

        act.Should().NotThrow<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    [Fact]
    public void A_null_filter_still_raises_the_ArgumentNullException_it_always_did()
    {
        // RequireFilter keeps the null case, and keeps its own exception type: the caller passed null for a
        // parameter declared non-nullable, which is a different mistake from asking for an unbounded scope.
        var act = () => SyncStore().Delete((Expression<Func<MgScopeModel, bool>>)null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
