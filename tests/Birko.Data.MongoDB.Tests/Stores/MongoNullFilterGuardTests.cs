using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.MongoDB.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// SH-M023 — the four MongoDB overrides that must repeat <c>AbstractBulkStore</c>'s null-filter guard,
/// and had no test.
///
/// <para>
/// <b>The mechanism.</b> The filter-based destructive overloads declare <c>filter</c> non-nullable and
/// never checked it. They are read-then-loop: <c>Delete(null!)</c> called <c>Read(null, …)</c>, where a
/// null filter legitimately means <i>read everything</i>, and deleted the entire result. Because every
/// statement they issue is per-row and therefore carries its own key, no backend query guard can see it —
/// the damage is "affected every row", never "a statement with no predicate". So it is refused at the
/// boundary, by <c>AbstractBulkStore.RequireFilter</c> / <c>AbstractAsyncBulkStore.RequireFilter</c>.
/// </para>
///
/// <para>
/// <b>Why these four need their own tests.</b> TASK-109 closed SH-M023 on the base classes, then had to
/// sweep: ten stores across three backends override the <b>public</b> <c>Delete(filter)</c> /
/// <c>Update(filter, …)</c> rather than the <c>protected *Core</c> methods, so they never reach the base
/// wrapper and must repeat the guard themselves. On MongoDB a null filter reaching the driver is an empty
/// predicate — <c>DeleteMany</c> over the whole collection. ElasticSearch's four overrides are covered by
/// CR-H047 and InMemory's two by <c>Birko.Data.InMemory.Tests@86df89c</c>; MongoDB's four rested on
/// inspection alone, so a refactor dropping one of the <c>RequireFilter</c> lines would break nothing.
/// The InMemory half of the very same sweep was <i>discovered</i> by tests — six of TASK-109's portable
/// tests failed against an already-correct base class — while the MongoDB half was found by grepping for
/// the pattern. That asymmetry is what this file removes.
/// </para>
///
/// <para>
/// <b>No live MongoDB, and no env gate.</b> <c>RequireFilter</c> throws before the <c>Collection == null</c>
/// check and before any driver call, and both stores have a parameterless constructor that leaves
/// <c>Client</c> null, so nothing here opens a socket. That is deliberate rather than incidental: the
/// regression suite for a destructive-write guard must not itself be destructive, and a suite that is
/// slow is evidence it is not offline (TASK-117).
/// </para>
/// </summary>
public class MongoNullFilterGuardTests
{
    /// <summary>
    /// Satisfies both constraints at once — the sync store requires <c>MongoDBModel</c>, the async store
    /// only <c>AbstractModel</c>, and <c>MongoDBModel</c> derives from it.
    /// </summary>
    public class MgGuardModel : MongoDBModel
    {
        public string? Label { get; set; }
    }

    /// <summary>No settings, so <c>Client</c> is null and <c>Collection</c> resolves to null.</summary>
    private static MongoDBStore<MgGuardModel> SyncStore() => new();

    private static AsyncMongoDBStore<MgGuardModel> AsyncStore() => new();

    private static PropertyUpdate<MgGuardModel> Updates()
        => new PropertyUpdate<MgGuardModel>().Set(x => x.Label, "changed");

    // ────────────────────────────────────────────── the four guards

    [Fact]
    public void Delete_with_a_null_filter_is_refused()
    {
        var store = SyncStore();

        var act = () => store.Delete((Expression<Func<MgGuardModel, bool>>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("filter");
    }

    [Fact]
    public void Update_with_a_null_filter_is_refused()
    {
        var store = SyncStore();

        var act = () => store.Update((Expression<Func<MgGuardModel, bool>>)null!, Updates());

        act.Should().Throw<ArgumentNullException>().WithParameterName("filter");
    }

    [Fact]
    public async Task DeleteAsync_with_a_null_filter_is_refused()
    {
        var store = AsyncStore();

        var act = () => store.DeleteAsync((Expression<Func<MgGuardModel, bool>>)null!);

        (await act.Should().ThrowAsync<ArgumentNullException>()).And.ParamName.Should().Be("filter");
    }

    [Fact]
    public async Task UpdateAsync_with_a_null_filter_is_refused()
    {
        var store = AsyncStore();

        var act = () => store.UpdateAsync((Expression<Func<MgGuardModel, bool>>)null!, Updates());

        (await act.Should().ThrowAsync<ArgumentNullException>()).And.ParamName.Should().Be("filter");
    }

    // ────────────────────────────────── the refusal has to name the deliberate door

    [Theory]
    [InlineData("delete", "DeleteAll()")]
    [InlineData("update", "UpdateAll(updates)")]
    public void The_refusal_names_the_all_rows_door(string operation, string door)
    {
        // § Conventions: "a guard that only says no gets reached around" — the message must point at the
        // way to express the intent it just refused. Parameterised over both operations because the base
        // picks the door name from the operation string, so a copy-paste there would name the wrong one.
        var store = SyncStore();

        Action act = operation == "delete"
            ? () => store.Delete((Expression<Func<MgGuardModel, bool>>)null!)
            : () => store.Update((Expression<Func<MgGuardModel, bool>>)null!, Updates());

        act.Should().Throw<ArgumentNullException>().WithMessage($"*{door}*");
    }

    // ────────────────────────── and the door it names has to open (SH-H037's opt-out rule)

    [Fact]
    public void DeleteAll_is_not_refused()
    {
        // The escape hatch the refusal advertises. TASK-117 shipped a guard whose named opt-out threw a
        // second, unrelated exception, so the message sent an operator into a wall — the opt-out is part
        // of the fix and needs its own test. Asserted as a bare NotThrow, NOT NotThrow<ArgumentNullException>:
        // a type-scoped negative passes on every other exception, which is exactly how that one hid.
        // Offline this is a no-op — InitCore is a no-op (MongoDB is schema-less) and ReadCore returns
        // empty when Collection is null — which is enough to prove the guard does not stand in its way.
        var store = SyncStore();

        var act = () => store.DeleteAll();

        act.Should().NotThrow();
    }

    [Fact]
    public void UpdateAll_is_not_refused()
    {
        var store = SyncStore();

        var act = () => store.UpdateAll(Updates());

        act.Should().NotThrow();
    }

    [Fact]
    public async Task DeleteAllAsync_is_not_refused()
    {
        var store = AsyncStore();

        var act = () => store.DeleteAllAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateAllAsync_is_not_refused()
    {
        var store = AsyncStore();

        var act = () => store.UpdateAllAsync(Updates());

        await act.Should().NotThrowAsync();
    }
}
