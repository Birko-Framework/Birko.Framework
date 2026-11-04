using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.JSON.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.JSON.Tests;

/// <summary>
/// A write to a store that was never configured is refused; a read still answers empty (TASK-464).
/// </summary>
/// <remarks>
/// <para>
/// Measured on the shipped code, and it was worse than filed: an unconfigured store accepted a
/// create, returned a real Guid, reported <c>Count == 1</c> and wrote nothing to disk — on every file
/// store, both twins, both formats. Each persistence method opened with a guard that
/// <c>return</c>ed when there was no path or name, so a lost write was reported as success. TASK-464
/// had predicted a <c>NullReferenceException</c> instead; the reality was silent, which is worse.
/// </para>
/// <para>
/// The contract is now one rule per operation class: <b>a write refuses, a read degrades</b>. The
/// read half is deliberate and is asserted below — § Conventions requires a write that cannot be
/// applied to fail loudly, and says nothing of the sort about a read, every one of which already
/// answered empty here.
/// </para>
/// </remarks>
public class UnconfiguredStoreRefusalTests
{
    // Every shape in the family, because they are three different classes in a chain and the middle
    // one overrides persistence — a guard that only covers the base is one the leaves can skip.
    public static TheoryData<string, Func<object>> SyncStores() => new()
    {
        { nameof(JsonStore<TestModel>), () => new JsonStore<TestModel>() },
        { nameof(JsonSeparateStore<TestModel>), () => new JsonSeparateStore<TestModel>() },
        { nameof(JsonBatchStore<TestModel>), () => new JsonBatchStore<TestModel>() },
    };

    [Theory]
    [MemberData(nameof(SyncStores))]
    public void A_create_on_an_unconfigured_store_is_refused(string name, Func<object> make)
    {
        dynamic store = make();

        Action act = () => store.Create(new TestModel { Name = "orphan" });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SetSettings*", "{0} must name the call that configures it", name);
    }

    [Theory]
    [MemberData(nameof(SyncStores))]
    public void A_refused_create_reports_failure_rather_than_a_silent_loss(string name, Func<object> make)
    {
        dynamic store = make();

        try
        {
            store.Create(new TestModel { Name = "orphan" });
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        // The defect this replaces: Create returned a Guid, Count said 1, and nothing reached disk.
        // Asserting the count is what distinguishes "refused" from "accepted and lost".
        // TASK-812: Count refuses an unconfigured store too, so the row is looked for once the store is configured.
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-812-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            store.SetSettings(name.Contains("Batch")
                ? new Birko.Data.JSON.Stores.BatchSettings { Location = dir, Name = "items" }
                : new Birko.Configuration.Settings(dir, "items"));
            ((long)store.Count()).Should().Be(0, "{0} must not hold a row it could not persist", name);
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task The_async_twin_refuses_too()
    {
        var store = new AsyncJsonSeparateStore<TestModel>();

        var act = async () => await store.CreateAsync(new TestModel { Name = "orphan" });

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*SetSettings*");
        // TASK-812: a read refuses too.
        await FluentActions.Awaiting(() => store.CountAsync()).Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// TASK-812 (rule 56, inverted; decided 2026-11-04 by the user): a read refuses as a write does. TASK-464 kept reads
    /// answering empty on purpose; an empty answer from a store that cannot look is as false as a write reported done.
    /// </summary>
    [Fact]
    public void A_read_on_an_unconfigured_store_is_refused_too()
    {
        var store = new JsonSeparateStore<TestModel>();

        store.Invoking(s => s.Read(null, null, null, null).ToList()).Should().Throw<InvalidOperationException>().WithMessage("*SetSettings*");
    }

    [Fact]
    public void A_configured_store_is_unaffected()
    {
        // The half that makes the refusal a fix rather than a blanket: if this stopped passing, the
        // guard would be refusing writes it has no business refusing.
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-464-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var store = new JsonSeparateStore<TestModel>();
            store.SetSettings(new Settings(dir, "items.json"));

            var id = store.Create(new TestModel { Name = "kept" });

            store.Read(id).Should().NotBeNull();
            store.Count().Should().Be(1);
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { /* ignored */ }
        }
    }

    [Fact]
    public void SetSettings_refuses_an_ISettings_it_cannot_use_instead_of_ignoring_it()
    {
        var store = new JsonSeparateStore<TestModel>();

        Action act = () => store.SetSettings(new NotASettings());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*NotASettings*",
                "ignoring it silently left the store unconfigured, which is how a caller ends up "
                + "believing both that they configured the store and that their writes were saved");
    }

    /// <summary>
    /// The only non-<c>Settings</c> implementation of <see cref="ISettings"/> that exists anywhere.
    /// </summary>
    /// <remarks>
    /// Measured before the refusal was added: across the framework and all consumer repos,
    /// <c>Settings</c> is the sole implementation — every other match is a
    /// <c>where TSettings : ISettings</c> constraint. So the refusal cannot fire for any type that
    /// ships today, and this test had to invent the first one. That is also why it was affordable:
    /// § SH-H037 requires the blast radius to be measured before turning silence into a throw.
    /// </remarks>
    private sealed class NotASettings : ISettings
    {
        public string GetId() => "not-a-settings";

        public void LoadFrom(ISettings data)
        {
        }
    }
}
