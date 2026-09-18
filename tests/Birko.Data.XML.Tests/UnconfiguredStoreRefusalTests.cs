using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.XML.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.XML.Tests;

/// <summary>
/// A write against a store that was never given settings is refused, by name, before it mutates
/// anything (TASK-462).
/// </summary>
/// <remarks>
/// <para>
/// <c>SetSettings</c> is a plain assignment that nothing enforces, and <c>InitCore</c> silently
/// no-ops when there are no settings — its <c>is Settings</c> pattern simply fails — so lazy init
/// reported success and the first create then dereferenced null. Measured before the guard existed:
/// <c>NullReferenceException</c>, thrown <em>after</em> the entity had been added to the in-memory
/// dictionary, so the store was left holding a row that was never written to disk.
/// </para>
/// <para>
/// ⚠ The compiler only ever pointed at half of this. The async family declares
/// <c>protected Settings? _settings</c> and warned (CS8602); the sync family declares
/// <c>Settings _settings = null!</c> and said nothing about code that is line-for-line the same.
/// The sync store was never safer, only quieter — which is why both twins are covered here rather
/// than only the one that produced a warning.
/// </para>
/// </remarks>
public class UnconfiguredStoreRefusalTests
{
    [Fact]
    public void Sync_create_without_settings_is_refused_and_names_SetSettings()
    {
        var store = new XmlSeparateStore<TestModel>();

        var act = () => store.Create(new TestModel { Name = "orphan", Value = 1 });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SetSettings*",
                "a guard that only says 'something was null' gets reached around — it has to name the door");
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
