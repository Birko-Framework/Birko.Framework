using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.Connectors;

/// <summary>
/// TASK-270 — <c>DataBase.GetConnector</c> caches a connector <b>process-wide</b> per (type, settings
/// id), so the object is reachable, shared and long-lived, and it keeps attracting state that belongs to
/// one caller or one operation. **Four** independent instances have now shipped on it:
///
/// <list type="number">
///   <item>the stores' unit-of-work enlistment put one caller's <c>DbTransaction</c> on it — concurrent
///         callers silently enlisted in each other's transaction (fixed by TASK-240's
///         <c>AmbientSqlTransaction</c>);</item>
///   <item>index-creation reporting put an append-only list on it, which grew one entry per HTTP request
///         forever and re-fired its event each time (fixed by keying it);</item>
///   <item>the migrations schema builder published one migration's connection <i>and</i> transaction and
///         never cleared them, so the runner disposed both and the next store's lazy schema-ensure ran on
///         a dead connection (fixed by TASK-259, which deleted the mechanism);</item>
///   <item><c>IsInitializing</c> — a plain mutable flag guarding <c>DoInit</c>, so while one flow was
///         inside its <c>OnInit</c> handlers a concurrent caller's initialisation was <b>silently
///         discarded</b> (fixed by TASK-270, now an <c>AsyncLocal</c> per instance).</item>
/// </list>
///
/// <para>
/// Four developers independently reached for "just put it on the connector". That is a design signal,
/// and § Conventions saying so is demonstrably not enough — instances two, three and four all shipped
/// <i>after</i> the reasoning was written down. **"I didn't add mutable state" is construction, not
/// evidence**, so this file is the evidence.
/// </para>
///
/// <para>
/// ⚠ <b>Scope, stated because it is smaller than it looks.</b> These tests see the abstract connector
/// types compiled into this assembly, which is where all four instances lived and where any shared
/// mechanism must live. A provider connector adding its own settable state in
/// <c>Birko.Data.SQL.MSSql</c> is not visible from here.
/// </para>
/// </summary>
public class ConnectorSharedStateTests
{
    /// <summary>
    /// The connector types the rule is about: the ones <c>DataBase.GetConnector</c> can actually cache
    /// and hand to every caller of a database.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Public and non-nested is the principled filter, not a convenient one.</b> Shared projects
    /// compile into the test assembly, so an unfiltered scan also sees the suite's own fakes — and a
    /// nested private test double cannot be cached by <c>GetConnector</c> (it instantiates a public type
    /// by <c>Type</c>) and is constructed fresh per test, so its mutable state is never shared with
    /// anyone. Excluding it removes a false positive rather than a finding.
    /// <see cref="The_scan_actually_finds_the_production_connectors"/> stops that filter quietly
    /// excluding everything.
    /// </remarks>
    private static IEnumerable<Type> ConnectorTypes =>
        typeof(AbstractConnectorBase).Assembly
            .GetTypes()
            .Where(t => typeof(AbstractConnectorBase).IsAssignableFrom(t))
            .Where(t => t.IsPublic && !t.IsNested);

    /// <summary>
    /// A filter that excluded everything would make every other test in this file vacuous, and it would
    /// do so silently. This is the control.
    /// </summary>
    [Fact]
    public void The_scan_actually_finds_the_production_connectors()
    {
        var names = ConnectorTypes.Select(t => t.Name).ToList();

        names.Should().Contain(nameof(AbstractConnectorBase));
        names.Should().Contain(nameof(AbstractConnector));
        names.Should().Contain(nameof(AbstractAsyncConnector));
    }

    /// <summary>
    /// State that is allowed to be settable on a shared connector, and why. Every entry is a decision
    /// somebody has to defend, which is the point of listing them rather than loosening the rule.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>An entry that starts passing must be removed, or the ledger becomes a blanket</b> — the same
    /// discipline § Conventions records for TASK-222's RavenDB ledger. That is what
    /// <see cref="Every_ledger_entry_is_still_needed"/> enforces.
    /// </remarks>
    private static readonly Dictionary<string, string> AcceptedSettableState = new()
    {
        ["RetryPolicy"] =
            "Per-DATABASE configuration, and the connector is cached per database, so one policy per "
            + "database is the correct scope rather than an accident. Deliberately NOT moved onto Settings: "
            + "Settings.GetId() is Location:Name(:UserName:Port) and carries neither this nor "
            + "CommandTimeout, so two settings objects differing only in retry policy already share one "
            + "connector and the first caller's value wins for everyone (the hazard TASK-276 pinned for "
            + "CommandTimeout). Relocating it there would hide the sharing rather than remove it. "
            + "Measured at TASK-270: 0 assignments in the framework's production code and in all 16 "
            + "consumer repos; the only writers are two tests of retry itself.",
    };

    /// <summary>
    /// The rule: a connector exposes no settable public or protected instance property, because such a
    /// property is one caller writing state that every other caller of the same database then reads.
    /// </summary>
    [Fact]
    public void No_connector_exposes_settable_public_or_protected_instance_state()
    {
        var offenders = new List<string>();

        foreach (var type in ConnectorTypes)
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic
                                                 | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var setter = p.GetSetMethod(nonPublic: true);
                if (setter == null || setter.IsPrivate) continue;
                if (AcceptedSettableState.ContainsKey(p.Name)) continue;

                offenders.Add($"{type.Name}.{p.Name} (setter: {(setter.IsPublic ? "public" : "protected/internal")})");
            }
        }

        offenders.Should().BeEmpty(
            "a connector is cached process-wide per (type, settings id), so settable instance state is one "
            + "caller writing what every other caller of the same database reads. Put per-operation state "
            + "on the flow (see AmbientSqlTransaction, and IsInitializing's AsyncLocal), or add a justified "
            + "entry to AcceptedSettableState explaining why sharing it is correct.");
    }

    /// <summary>
    /// Instance three's shape, as a regression guard: <c>SetExternalTransaction(connection, transaction)</c>
    /// published one migration's per-operation objects onto the shared connector. TASK-259 deleted it, and
    /// this is what stops it — or anything shaped like it — coming back.
    /// </summary>
    [Fact]
    public void No_connector_accepts_a_connection_or_transaction_through_public_surface()
    {
        var perOperation = new[] { typeof(DbConnection), typeof(DbTransaction) };
        var offenders = new List<string>();

        foreach (var type in ConnectorTypes)
        {
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                              | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsPrivate || m.IsSpecialName) continue;

                // A method that RECEIVES a connection to run something on is fine -- that is the caller
                // handing over an object for the duration of the call. The defect is a method that STORES
                // one, and the tell is a void/Task setter-shaped name.
                if (!m.Name.StartsWith("Set", StringComparison.Ordinal)) continue;

                if (m.GetParameters().Any(x => perOperation.Any(t => t.IsAssignableFrom(x.ParameterType))))
                {
                    offenders.Add($"{type.Name}.{m.Name}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "publishing a caller's connection or transaction onto the process-wide connector is TASK-259's "
            + "defect: the caller disposed both and left them on the shared object, so the next store's "
            + "schema-ensure ran on a dead connection and that store stayed permanently uninitialised. "
            + "Per-operation context travels with the flow -- see AmbientSqlTransaction.");
    }

    /// <summary>
    /// A ledger that keeps an entry nobody needs stops being a record and becomes a blanket.
    /// </summary>
    [Fact]
    public void Every_ledger_entry_is_still_needed()
    {
        var declared = ConnectorTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic
                                             | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => p.GetSetMethod(nonPublic: true) is { IsPrivate: false })
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in AcceptedSettableState.Keys)
        {
            declared.Should().Contain(entry,
                $"'{entry}' is listed as accepted settable state but no connector declares it as such any "
                + "more -- delete the ledger entry, or the exemption silently covers something else later");
        }
    }

    /// <summary>
    /// The fixed instance four, asserted as behaviour rather than as shape: re-entrancy is per call flow,
    /// so one flow being inside <c>DoInit</c> must not suppress another flow's initialisation.
    /// </summary>
    [Fact]
    public async Task One_flow_initialising_does_not_suppress_another_flows_init()
    {
        var connector = new ProbeConnector();
        var insideFirst = new SemaphoreSlim(0, 1);
        var releaseFirst = new SemaphoreSlim(0, 1);

        connector.OnInit += _ =>
        {
            Interlocked.Increment(ref connector.InitCount);
            // Only the first flow parks; the second must not be blocked by it.
            if (Volatile.Read(ref connector.InitCount) == 1)
            {
                insideFirst.Release();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }
        };

        var first = Task.Run(() => connector.DoInit());
        (await insideFirst.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue("the first flow should enter OnInit");

        // Second flow, while the first is still inside its handler. With the old shared flag this saw
        // IsInitializing == true and returned having done nothing at all.
        await Task.Run(() => connector.DoInit());

        releaseFirst.Release();
        await first;

        connector.InitCount.Should().Be(2, "each flow's init must run; the guard is per flow, not per connector");
    }

    /// <summary>
    /// The half the old assignment pair could not do: a throwing handler must not leave the connector
    /// permanently unable to initialise. Before TASK-270 the flag was set true, the handler threw, and the
    /// reset line never ran -- so <c>DoInit</c> was suppressed for every caller for the life of the process.
    /// </summary>
    [Fact]
    public void A_throwing_init_handler_does_not_wedge_the_connector()
    {
        var connector = new ProbeConnector();
        var shouldThrow = true;

        connector.OnInit += _ =>
        {
            Interlocked.Increment(ref connector.InitCount);
            if (shouldThrow) throw new InvalidOperationException("handler blew up");
        };

        Action first = () => connector.DoInit();
        first.Should().Throw<InvalidOperationException>();

        connector.IsInitializing.Should().BeFalse("the scope restores the flag even when a handler throws");

        shouldThrow = false;
        connector.DoInit();
        connector.InitCount.Should().Be(2, "a later init must still run");
    }

    private sealed class ProbeConnector : AbstractConnector
    {
        public int InitCount;

        public ProbeConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public override DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException("not needed: these tests never open a connection");

        public override string ConvertType(System.Data.DbType type, Birko.Data.SQL.Fields.AbstractField field) => "TEXT";

        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field) => field.Name + " TEXT";
    }
}
