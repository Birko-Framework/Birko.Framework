using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.XML.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.XML.Tests;

/// <summary>
/// A write against a store that was never given settings is refused, by name, before it mutates
/// anything (TASK-462, widened by TASK-464).
/// </summary>
/// <remarks>
/// <para>
/// <c>SetSettings</c> is a plain assignment that nothing enforces, and <c>InitCore</c> silently
/// no-ops when there are no settings — its <c>is Settings</c> pattern simply fails — so lazy init
/// reported success and the store then had no path to write to.
/// </para>
/// <para>
/// ⚠ The compiler only ever pointed at one line of this. The async family declares
/// <c>protected Settings? _settings</c> and warned (CS8602) about the separate store's create; the
/// sync family declares <c>Settings _settings = null!</c> and said nothing about code that is
/// line-for-line the same. The sync store was never safer, only quieter.
/// </para>
/// <para>
/// ⚠ And the warning was pointing at the <em>least</em> harmful instance. TASK-462 fixed that one
/// create path, where the failure was at least loud. TASK-464 then measured the rest: every other
/// write on an unconfigured store — both twins, both formats, all three store shapes — accepted the
/// data, returned a real Guid, reported <c>Count == 1</c> and wrote nothing to disk, because each
/// persistence method opened with a guard that simply <c>return</c>ed. A lost write reported as
/// success. So the tests below assert the row count, not just the exception: it is the count that
/// separates "refused" from "accepted and silently discarded".
/// </para>
/// </remarks>
public class UnconfiguredStoreRefusalTests
{
    /// <summary>
    /// Every shape in the family, because they are three classes in a chain and the middle one
    /// overrides persistence — a guard that only covers the base is one the leaves can skip.
    /// </summary>
    public static TheoryData<string, Func<object>> SyncStores() => new()
    {
        { nameof(XmlStore<TestModel>), () => new XmlStore<TestModel>() },
        { nameof(XmlSeparateStore<TestModel>), () => new XmlSeparateStore<TestModel>() },
        { nameof(XmlBatchStore<TestModel>), () => new XmlBatchStore<TestModel>() },
    };

    [Theory]
    [MemberData(nameof(SyncStores))]
    public void Sync_create_without_settings_is_refused_and_names_SetSettings(string name, Func<object> make)
    {
        dynamic store = make();

        Action act = () => store.Create(new TestModel { Name = "orphan", Value = 1 });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SetSettings*",
                "{0}: a guard that only says 'something was null' gets reached around — it has to name the door",
                name);
    }

    [Theory]
    [MemberData(nameof(SyncStores))]
    public void A_refused_create_reports_failure_rather_than_a_silent_loss(string name, Func<object> make)
    {
        dynamic store = make();

        try
        {
            store.Create(new TestModel { Name = "orphan", Value = 1 });
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        // TASK-464 measured the shipped behaviour: Create returned a Guid, Count said 1, and nothing
        // reached disk. Asserting the count is what separates "refused" from "accepted and lost".
        ((long)store.Count()).Should().Be(0, "{0} must not hold a row it could not persist", name);
    }

    [Fact]
    public void SetSettings_refuses_an_ISettings_it_cannot_use_instead_of_ignoring_it()
    {
        var store = new XmlSeparateStore<TestModel>();

        Action act = () => store.SetSettings(new NotASettings());

        act.Should().Throw<ArgumentException>().WithMessage("*NotASettings*");
    }

    /// <summary>The only non-<c>Settings</c> implementation of ISettings that exists anywhere.</summary>
    /// <remarks>
    /// Measured before the refusal was added: <c>Settings</c> is the sole implementation across the
    /// framework and all consumer repos — every other match is a <c>where TSettings : ISettings</c>
    /// constraint. So it cannot fire for any shipping type, and this test had to invent the first one.
    /// </remarks>
    private sealed class NotASettings : Birko.Configuration.ISettings
    {
        public string GetId() => "not-a-settings";

        public void LoadFrom(Birko.Configuration.ISettings data)
        {
        }
    }

    [Fact]
    public async Task Async_create_without_settings_is_refused_and_names_SetSettings()
    {
        var store = new AsyncXmlSeparateStore<TestModel>();

        var act = async () => await store.CreateAsync(new TestModel { Name = "orphan", Value = 1 });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*SetSettings*");
    }

    [Fact]
    public void The_refusal_replaces_a_NullReferenceException_rather_than_adding_one()
    {
        var store = new XmlSeparateStore<TestModel>();

        // The distinction matters: an NRE says nothing about what the caller should do, and this
        // path used to produce one. Asserting the *absence* of NRE is what fails if someone
        // "simplifies" the guard back into a bare dereference.
        var act = () => store.Create(new TestModel { Name = "orphan", Value = 1 });

        act.Should().NotThrow<NullReferenceException>();
    }

    [Fact]
    public void A_refused_create_leaves_no_row_behind()
    {
        var store = new XmlSeparateStore<TestModel>();

        try
        {
            store.Create(new TestModel { Name = "orphan", Value = 1 });
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        // This is the half that makes the guard's PLACEMENT load-bearing rather than cosmetic: the
        // old code added to the dictionary and then threw, so a caller catching the failure was left
        // with an in-memory row that no file backed. Moving the guard after the Add would still
        // produce the right exception and still fail this test.
        store.Count().Should().Be(0);
    }

    [Fact]
    public async Task A_refused_async_create_leaves_no_row_behind()
    {
        var store = new AsyncXmlSeparateStore<TestModel>();

        try
        {
            await store.CreateAsync(new TestModel { Name = "orphan", Value = 1 });
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        (await store.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// Reads answer empty rather than refusing, and that asymmetry is deliberate.
    /// </summary>
    /// <remarks>
    /// § Conventions: a write that cannot be applied must never report success, so it throws. A read
    /// has no such obligation and every other path in these stores already returns empty for an
    /// unconfigured store — unifying the two from symmetry would turn a working degrade into a
    /// start-up failure for anything that probes a store before configuring it.
    /// </remarks>
    [Fact]
    public void A_read_on_an_unconfigured_store_still_answers_empty()
    {
        var store = new XmlSeparateStore<TestModel>();

        store.Invoking(s => s.Read(null, null, null, null).ToList())
            .Should().NotThrow();
    }
}
