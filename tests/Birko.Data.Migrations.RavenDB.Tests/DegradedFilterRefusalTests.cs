using System;
using System.Collections.Generic;
using Birko.Data.Exceptions;
using Birko.Data.Migrations.RavenDB.Context;
using FluentAssertions;
using Raven.Client.Documents;
using Xunit;
using System.IO;

namespace Birko.Data.Migrations.RavenDB.Tests;

/// <summary>
/// <b>SH-H032 (TASK-314).</b> <c>{"status":{}}</c> takes <see cref="RavenDBDataMigrator.ParseFilterToRql"/>'s
/// object branch and its operator loop adds nothing, so the clause came back empty and the caller sent
/// <c>FROM 'Collection'</c> with no <c>WHERE</c> — a <c>DeleteByQueryOperation</c> over the whole
/// collection, and a <c>PatchByQueryOperation</c> over every document.
///
/// <para>
/// The refusal is raised before <c>_store.Operations.Send</c>, so no live server is required — the same
/// property this project's existing <c>CopyData</c> test relies on.
/// </para>
/// </summary>
public class DegradedFilterRefusalTests
{
    /// <summary>A store pointed at a closed port: reaching it would fail loudly, not pass quietly.</summary>
    private static DocumentStore Store()
        => new() { Urls = new[] { "http://localhost:59999" }, Database = "unused" };

    [Fact]
    public void A_delete_whose_filter_constrains_nothing_is_refused_before_touching_the_server()
    {
        using var store = Store();
        var migrator = new RavenDBDataMigrator(store);

        Action act = () => migrator.DeleteDocuments("Widgets", "{\"status\":{}}");

        var ex = act.Should().Throw<WholeTableWriteException>().Which;
        ex.Operation.Should().Be("delete");
        ex.TableName.Should().Be("Widgets");
        ex.Message.Should().Contain("every document in the collection");
    }

    [Fact]
    public void An_update_whose_filter_constrains_nothing_is_refused_before_touching_the_server()
    {
        using var store = Store();
        var migrator = new RavenDBDataMigrator(store);

        Action act = () => migrator.UpdateDocuments(
            "Widgets", "{\"status\":{}}", new Dictionary<string, object> { ["Name"] = "x" });

        act.Should().Throw<WholeTableWriteException>()
            .Which.Operation.Should().Be("update");
    }

    // ---------------------------------------------------------------- contract pins
    // Without these, "refuse every filter" passes everything above and looks like a valid fix. Both get
    // past the guard and then fail on the uninitialised/unreachable store — which is the proof.

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    public void The_explicit_match_all_door_is_not_refused(string? filterJson)
    {
        using var store = Store();
        var migrator = new RavenDBDataMigrator(store);

        Action act = () => migrator.DeleteDocuments("Widgets", filterJson!);

        act.Should().NotThrow<WholeTableWriteException>();
    }

    [Fact]
    public void An_operator_filter_is_not_refused()
    {
        using var store = Store();
        var migrator = new RavenDBDataMigrator(store);

        Action act = () => migrator.DeleteDocuments("Widgets", "{\"age\":{\"$gt\":18}}");

        act.Should().NotThrow<WholeTableWriteException>();
    }

    /// <summary>
    /// <c>CountDocuments</c> takes a different translator (<c>ApplyFilterToQuery</c>, which drives the
    /// <c>IDocumentQuery</c> builder rather than composing RQL text) and had the identical defect. It is
    /// guarded on the same terms so a count cannot answer for the whole collection while a delete built
    /// from the same filter is refused (§ TASK-215, § TASK-313) — but unlike the two write paths it opens
    /// a session first, so exercising it needs a live server and it is covered by source shape here.
    /// A scan is weaker than a behavioural assertion and is the honest alternative to no cover at all.
    /// </summary>
    [Fact]
    public void The_count_path_is_wired_to_the_same_guard()
    {
        var source = System.IO.File.ReadAllText(RavenMigratorSourcePath());

        // Both halves, because they fail independently: the guard can be wired to a helper that always
        // claims it constrained something. Measured — asserting only the call site leaves a mutation of
        // ApplyFilterToQuery's return to `true` failing nothing at all.
        source.Should().Contain("MigrationFilter.RequireBounded(filterJson, applied, \"count\"",
            "the count path has to consult the guard");
        source.Should().Contain("return applied > 0;",
            "and the helper has to report what it actually applied, not a constant");
    }

    private static string RavenMigratorSourcePath()
    {
        // Walk up looking for the project directory, trying both the level itself and a
        // "Framework" child at that level. A FIXED depth breaks whenever the layout changes: the
        // monorepo migration added a "tests/" segment, so the old `for i<6` landed one level too
        // deep and produced .../Framework/Framework/... (AppContext.BaseDirectory has a trailing
        // separator, so the first GetDirectoryName only strips it). This shape survives both.
        for (var probe = new DirectoryInfo(AppContext.BaseDirectory); probe != null; probe = probe.Parent)
        {
            foreach (var root in new[] { probe.FullName, Path.Combine(probe.FullName, "Framework") })
            {
                var candidate = Path.Combine(root, "Birko.Data.Migrations.RavenDB", "Context", "RavenDBDataMigrator.cs");
                if (File.Exists(candidate)) return candidate;
            }
        }
        throw new FileNotFoundException("the source scan must actually find RavenDBDataMigrator.cs");
    }
}
