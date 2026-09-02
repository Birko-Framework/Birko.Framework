using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-295 — <b>a provider override cannot skip the created-table recording, demonstrated rather than
/// asserted in prose.</b>
///
/// <para><c>RecordTableCreated</c> used to be called from the <b>virtual</b>
/// <c>CreateTable(string, IEnumerable&lt;string&gt;)</c>, i.e. from the very method every provider
/// overrides. PostgreSQL, MySQL, SQL Server and TimescaleDB all did, so <c>TablesCreated</c> was
/// permanently empty on four of five connectors and TASK-286's annotation, TASK-287's escape channel and
/// TASK-288's healing were inert there. Fifth instance of § TASK-243's <i>"a funnel with four overrides
/// is not a funnel"</i>.</para>
///
/// <para>The recording now lives in a <b>non-virtual</b> public wrapper that calls a
/// <c>protected virtual CreateTableCore</c>. This suite is the part that keeps it that way: the live
/// per-provider suites prove the four shipped connectors record, and these prove that a <i>new</i> one
/// cannot fail to — which is the property that was actually missing, and the one no per-provider test can
/// establish.</para>
/// </summary>
public class CreateTableRecordingTests
{
    /// <summary>
    /// A connector that overrides the emitter exactly as the four shipped providers do, and emits nothing
    /// so the test needs no database.
    /// </summary>
    private sealed class OverridingConnector : AbstractConnector
    {
        public OverridingConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public readonly List<string> Emitted = new();
        public bool ThrowFromCore { get; set; }

        protected override void CreateTableCore(string name, IEnumerable<string> fields)
        {
            if (ThrowFromCore)
            {
                throw new InvalidOperationException("emitter failed");
            }
            Emitted.Add(name);
        }

        public override DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(DbType type, Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
    }

    [Fact]
    public void A_provider_that_overrides_the_emitter_still_records_the_created_table()
    {
        var connector = new OverridingConnector();

        connector.CreateTable("Widgets", new[] { "Guid TEXT" });

        connector.Emitted.Should().ContainSingle().Which.Should().Be("Widgets",
            "the override ran, so this really is the shape that used to skip the recording");
        connector.TablesCreated.Keys.Should().Contain("Widgets",
            "and it recorded anyway, because the recording is in a wrapper the override never sees. "
            + "This is the property that was missing: four shipped providers overrode the recording away, "
            + "and nothing failed when they did");
    }

    /// <summary>
    /// The structural half. Without this, a later edit that makes the wrapper virtual again — or moves the
    /// recording back into it — reopens TASK-295 silently, and only a live per-provider run would notice.
    /// </summary>
    [Fact]
    public void The_public_CreateTable_wrapper_is_not_virtual_and_the_emitter_is()
    {
        var wrapper = typeof(AbstractConnector).GetMethod(
            "CreateTable", BindingFlags.Public | BindingFlags.Instance,
            null, new[] { typeof(string), typeof(IEnumerable<string>) }, null);

        wrapper.Should().NotBeNull();
        wrapper!.IsVirtual.Should().BeFalse(
            "a virtual wrapper is exactly what let four providers override the bookkeeping away. Change "
            + "the emitter (CreateTableCore), never this");

        var core = typeof(AbstractConnector).GetMethod(
            "CreateTableCore", BindingFlags.NonPublic | BindingFlags.Instance);

        core.Should().NotBeNull("providers need a seam, or they cannot change the statement at all");
        core!.IsVirtual.Should().BeTrue();
    }

    /// <summary>
    /// TASK-286's contract, preserved through the refactor: a create that <b>threw</b> is not recorded, so
    /// <c>TablesCreated</c> never claims a table whose DDL failed.
    /// </summary>
    [Fact]
    public void A_create_that_throws_is_not_recorded()
    {
        var connector = new OverridingConnector { ThrowFromCore = true };

        var act = () => connector.CreateTable("Widgets", new[] { "Guid TEXT" });

        act.Should().Throw<InvalidOperationException>();
        connector.TablesCreated.Should().BeEmpty(
            "the recording sits after the emitter returns, deliberately — TASK-286. Note what it still "
            + "does NOT mean: a create inside a caller's transaction that later rolls back stays recorded, "
            + "because the question is 'was this ever created, and when'");
    }

    /// <summary>
    /// And the dispatcher records each table separately, so a multi-table schema-ensure is not one entry.
    /// </summary>
    [Fact]
    public void Each_table_in_a_multi_table_create_is_recorded()
    {
        var connector = new OverridingConnector();

        connector.CreateTable("Alpha", new[] { "Guid TEXT" });
        connector.CreateTable("Beta", new[] { "Guid TEXT" });

        connector.TablesCreated.Keys.Should().BeEquivalentTo(new[] { "Alpha", "Beta" });
    }
}
