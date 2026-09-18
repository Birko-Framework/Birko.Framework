using System;
using System.IO;
using System.Linq;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InMemory.Tests;

/// <summary>
/// SH-H046 — <c>IStore.Destroy()</c> / <c>IAsyncStore.DestroyAsync()</c> were documented as
/// <i>"destroys the store and releases all resources"</i>, which reads as disposal, while every
/// implementation permanently deletes data.
/// </summary>
/// <remarks>
/// <para>
/// The blast radius is per backend and wider than this entity on two of them:
/// <c>RavenDBStore.Destroy</c> sends <c>DeleteDatabasesOperation(hardDelete: true)</c> and drops the
/// <b>entire database</b>; CosmosDB deletes the container, MongoDB drops the collection, SQL drops the
/// table, JSON and XML delete the file, InMemory clears its dictionary.
/// </para>
/// <para>
/// ⚠ <b>The harvest's supporting claim was wrong, and correcting it sharpened the fix.</b> It said
/// <i>"no store implements IDisposable, so a consumer looking for cleanup finds only Destroy()"</i>.
/// Measured: <c>RavenDBStore</c>, <c>InfluxDBStore</c>, <c>DataBaseStore</c> and their async twins all
/// implement it, releasing exactly the resources the old doc comment claimed. So the doc did not merely
/// mislead — it described <b>what an existing, correct member already does</b>, giving a consumer no
/// reason to look past it. The defect is on the contract, not on the implementations.
/// </para>
/// <para>
/// This file therefore pins two different things: the <b>behaviour</b>, so the documentation cannot
/// drift back into a lie, and the <b>documentation</b> itself, because a prose fix with no test is a
/// prose fix somebody reverts while tidying. Hosted here following the precedent of
/// <c>PortableBulkFilterGuardTests</c> — there is no <c>Birko.Data.Stores.Tests</c>, and InMemory is
/// the canonical test double for the shared store bases.
/// </para>
/// </remarks>
public class DestroyIsNotDisposalTests
{
    private class Thing : AbstractModel
    {
        public string? Name { get; set; }
    }

    private sealed class Store : AbstractInMemoryStore<Thing>
    {
    }

    private sealed class AsyncStore : AbstractAsyncInMemoryStore<Thing>
    {
    }

    // ── The behaviour the documentation now describes ──────────────────────────
    //
    // ⚠ Deliberately NOT re-asserted here. `InMemoryStoreTests.Destroy_ShouldClearAllData` and its
    // async twin in `AsyncInMemoryStoreTests` already pin it, and a duplicate is noise. Measured:
    // making InMemory's `Destroy` a no-op reds those two and nothing in this file — which is the
    // honest attribution. What this file adds is everything the behaviour tests cannot say: that the
    // contract offers no disposal member, and that the documentation describes the behaviour rather
    // than contradicting it.

    [Fact]
    public void Destroy_is_NOT_scoped_by_any_filter()
    {
        // Worth its own assertion: the doc now says "nothing is scoped to a filter", and the reason a
        // reader might assume otherwise is that every other destructive member on a bulk store takes
        // one. Destroy has no overload that does.
        typeof(IBaseStore).GetMethods()
            .Where(m => m.Name == "Destroy")
            .Should().OnlyContain(m => m.GetParameters().Length == 0,
                "there is no filtered Destroy — use IBulkDeleteStore<T>.Delete(filter) for that");
    }

    // ── The contract, which is where the defect lived ──────────────────────────

    [Fact]
    public void The_store_CONTRACT_offers_no_disposal_member_which_is_why_the_doc_mattered()
    {
        // The doc's redirect ("use IDisposable where the store implements it") is only honest because
        // disposal is NOT on the contract — it is a per-implementation concern. If a future change adds
        // IDisposable to IStore, this test fails and the doc must be re-worded rather than left stale.
        foreach (var contract in new[] { typeof(IBaseStore), typeof(IStore<Thing>), typeof(IAsyncBaseStore), typeof(IAsyncStore<Thing>) })
        {
            typeof(IDisposable).IsAssignableFrom(contract).Should().BeFalse(
                $"{contract.Name} declares no disposal member, so Destroy() was the only cleanup-looking "
                + "thing a consumer could find — that is the whole mechanism of SH-H046");
        }

        foreach (var baseType in new[] { typeof(AbstractInMemoryStore<Thing>), typeof(AbstractAsyncInMemoryStore<Thing>) })
        {
            typeof(IDisposable).IsAssignableFrom(baseType).Should().BeFalse();
        }
    }

    // ── The documentation, pinned so it cannot drift back ─────────────────────

    [Theory]
    [InlineData("IStore.cs", "Destroy")]
    [InlineData("IAsyncStore.cs", "DestroyAsync")]
    public void The_interface_doc_does_not_describe_Destroy_as_releasing_resources(string file, string member)
    {
        var source = File.ReadAllText(Path.Combine(StoresSourceDirectory(), file));

        source.Should().Contain(member, "the scan must be looking at the right file");

        // ⚠ The phrase must be absent as a DESCRIPTION, but the remarks legitimately quote it to
        // record what the member used to claim. So scan for an UNQUOTED occurrence: a line carrying the
        // phrase outside an <i>"..."</i> historical quotation. A flat NotContain here matches the
        // explanation and fails for the wrong reason - measured, twice, before this was narrowed.
        WithoutQuotations(source).Should().NotContain("releases all resources",
            "SH-H046: that wording reads as disposal, and it describes what IDisposable already does on "
            + "the stores that hold resources — so a consumer had no reason to look past Destroy(). "
            + "Quoting it inside <i>…</i> to record the history is fine; describing the member with it "
            + "is the defect.");

        source.Should().Contain("PERMANENTLY DELETES",
            "the doc has to say what the method does before it says anything else; a warning buried "
            + "below a reassuring summary is the shape that caused this");
    }

    [Fact]
    public void The_interface_doc_names_the_doors_a_caller_actually_has()
    {
        // CLAUDE.md § SH-H037: a warning that only says "this is dangerous" gets reached around. The
        // alternatives were verified to exist before being named here — Delete(filter) is on
        // IBulkDeleteStore<T>, DeleteAll() is on AbstractBulkStore<T> and deliberately NOT on the
        // interface, and that distinction is stated in the doc rather than glossed.
        var sync = File.ReadAllText(Path.Combine(StoresSourceDirectory(), "IStore.cs"));
        var async = File.ReadAllText(Path.Combine(StoresSourceDirectory(), "IAsyncStore.cs"));

        sync.Should().Contain("IDisposable").And.Contain("Delete(filter)").And.Contain("DeleteAll()");
        async.Should().Contain("IDisposable").And.Contain("DeleteAsync(filter)").And.Contain("DeleteAllAsync()");

        // And the doors are real — asserted against the types, not just the prose.
        typeof(IBulkDeleteStore<Thing>).GetMethods()
            .Should().Contain(m => m.Name == "Delete" && m.GetParameters().Length == 1,
                "Delete(filter) is named in the doc, so it must exist on the interface named");
        typeof(AbstractInMemoryStore<Thing>).GetMethod("DeleteAll")
            .Should().NotBeNull("DeleteAll() is named in the doc as a base-class member");
    }

    /// <summary>
    /// Removes every <c>&lt;i&gt;…&lt;/i&gt;</c> span so the scan reads the prose that DESCRIBES the
    /// member, not the prose that QUOTES what it used to claim.
    /// </summary>
    /// <remarks>
    /// ⚠ A line-based filter is not enough, and the first version of this used one: a remark can wrap
    /// its quotation across two lines, leaving the continuation carrying the phrase with no opening
    /// tag on it. It passed here by luck — the store interfaces happen to keep each quotation on one
    /// line — and failed on the repository contract, whose async remark wraps. The sibling file
    /// `Birko.Data.Repositories.Tests/DestroyIsNotDisposalDocTests` uses this same rule deliberately:
    /// two files checking one thing two different ways is what this codebase keeps having to unpick.
    /// </remarks>
    private static string WithoutQuotations(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        int i = 0;
        while (i < source.Length)
        {
            int open = source.IndexOf("<i>", i, StringComparison.Ordinal);
            if (open < 0)
            {
                result.Append(source, i, source.Length - i);
                break;
            }

            result.Append(source, i, open - i);
            int close = source.IndexOf("</i>", open, StringComparison.Ordinal);
            i = close < 0 ? source.Length : close + "</i>".Length;
        }

        return result.ToString();
    }

    private static string StoresSourceDirectory()
    {
        // .../Framework.Tests/Birko.Data.InMemory.Tests/bin/Debug/net10.0 -> the sibling Framework tree.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            foreach (var root in new[] { dir.FullName, Path.Combine(dir.FullName, "Framework") })
            {
                var stores = Path.Combine(root, "Birko.Data.Stores");
                if (Directory.Exists(stores))
                {
                    return stores;
                }
            }
        }

        throw new DirectoryNotFoundException(
            "Birko.Data.Stores was not findable from the test binary; these scans are the only thing "
            + "stopping the doc drifting back, so do not weaken them into a silent skip.");
    }
}
