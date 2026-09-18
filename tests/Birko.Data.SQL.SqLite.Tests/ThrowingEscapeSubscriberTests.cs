using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-289 — a diagnostic subscriber that <b>throws</b> cannot change what the caller sees.
///
/// <para><b>What was measured before the fix.</b> <c>OnSchemaEscapeDetected</c> was raised from inside
/// <c>EnsureSchemaAndReport</c>, between building TASK-286's annotated exception and throwing it, so a
/// handler that threw <b>replaced</b> that exception:</para>
///
/// <list type="bullet">
/// <item>the write threw the handler's exception with the <b>annotation gone</b> — the instrument
/// destroyed by the handler written to read it;</item>
/// <item>⚠ and the replacement no longer satisfied <c>SelectCount</c>'s
/// <c>catch … when (IsMissingTableExceptionChain(ex))</c>, so the <b>exception filter stopped matching</b>,
/// the catch never ran, and the count <b>threw instead of returning 0</b> — TASK-285 reopened from outside
/// the framework, by a host doing nothing worse than escalating.</item>
/// </list>
///
/// <para>⚠ <b>The trigger is ordinary, not exotic.</b> The event's own summary invites a host to
/// "subscribe to log or escalate", and escalating by rethrowing is a normal thing to write — which is
/// exactly what the Symbio-side task for TASK-287/288 is about to ask someone to do.</para>
///
/// <para>⚠ <b>Fixed while the channel had ZERO consumers.</b> Its sibling <c>OnIndexCreationFailed</c> has
/// the identical hole and is deliberately untouched: it is consumed in Symbio's production code, host,
/// tests and specs, so changing whether a handler's exception propagates there is a behaviour change on
/// consumed surface needing its own measurement. TASK-283 owns it; <c>RaiseDiagnostic</c> is the candidate
/// answer for it to adopt, not a second policy.</para>
/// </summary>
public class ThrowingEscapeSubscriberTests : IDisposable
{
    private readonly string _root;

    public ThrowingEscapeSubscriberTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-throwsub-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class TRow : AbstractModel { public string? Name { get; set; } }

    private sealed class TRowMapping : IModelMapping<TRow>
    {
        public void Configure(ModelMap<TRow> map)
        {
            map.ToTable("ThrowSubRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(50);
        }
    }

    private void Register()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new TRowMapping());
        registry.ApplyToDatabase();
    }

    private SqLiteConnector Connector()
        => (SqLiteConnector)new SqLiteStoreFactory(
            new SqLiteStoreFactoryOptions { Location = _root, Name = "throwsub.db" }).GetConnector();

    /// <summary>Create the table so the connector records it, then make it vanish — TASK-286's anomaly.</summary>
    private SqLiteConnector ConnectorWithAVanishedTable()
    {
        Register();
        var connector = Connector();
        connector.CreateTable(new[] { typeof(TRow) });
        connector.DropTable(new[] { typeof(TRow) });
        return connector;
    }

    private static Action<SchemaEscape> Throwing()
        => _ => throw new InvalidOperationException("host escalated");

    // ── the defect: a handler's exception must not reach the caller ──────────────────────────────────

    [Fact]
    public void AThrowingSubscriber_DoesNotMakeTheCountThrow()
    {
        var connector = ConnectorWithAVanishedTable();
        connector.OnSchemaEscapeDetected += Throwing();

        var count = connector.SelectCount(typeof(TRow));

        count.Should().Be(0,
            "before TASK-289 this threw the HANDLER's exception: it replaced the annotated one, which no "
            + "longer satisfied the catch's `when (IsMissingTableExceptionChain(ex))` filter, so the catch "
            + "never ran. A host could reopen TASK-285's intermittent 500 without touching the framework");
    }

    [Fact]
    public async Task AThrowingSubscriber_DoesNotMakeTheASYNCCountThrow()
    {
        var connector = ConnectorWithAVanishedTable();
        connector.OnSchemaEscapeDetected += Throwing();

        var count = await connector.SelectCountAsync(
            typeof(TRow), (IEnumerable<Birko.Data.SQL.Conditions.Condition>?)null, CancellationToken.None);

        count.Should().Be(0, "the async count is a separate code path with the same exception filter");
    }

    [Fact]
    public void AThrowingSubscriber_DoesNotEatTheWritePathAnnotation()
    {
        var connector = ConnectorWithAVanishedTable();
        connector.OnSchemaEscapeDetected += Throwing();

        var act = () => connector.Insert(new TRow { Guid = Guid.NewGuid(), Name = "x" });

        var thrown = act.Should().Throw<Exception>("the write must still be REPORTED — TASK-277").Which;
        thrown.Message.Should().Contain("but this connector already created it",
            "before TASK-289 the caller got `host escalated` and the diagnostic was gone — the instrument "
            + "destroyed by the handler written to read it");
        thrown.Should().NotBeOfType<InvalidOperationException>(
            "the handler's exception must not be what the caller sees");
    }

    // ── swallowed means RECORDED, not discarded ─────────────────────────────────────────────────────

    [Fact]
    public void TheHandlersOwnFailure_IsRecorded_NotDiscarded()
    {
        var connector = ConnectorWithAVanishedTable();
        connector.OnSchemaEscapeDetected += Throwing();

        connector.SelectCount(typeof(TRow));

        var failure = connector.SubscriberFailures.Should().ContainSingle(
            "a channel that hid a handler's own defect would be the same failure one level up — and a "
            + "broken handler is otherwise indistinguishable from an event that never fired").Which;
        failure.Channel.Should().Be("OnSchemaEscapeDetected");
        failure.Error.Should().BeOfType<InvalidOperationException>();
        failure.Error.Message.Should().Be("host escalated");
    }

    [Fact]
    public void TheFailureSinkRaisesNoEventOfItsOwn()
    {
        typeof(AbstractConnector).GetEvents()
            .Select(e => e.Name)
            .Should().NotContain(n => n.Contains("SubscriberFailure", StringComparison.OrdinalIgnoreCase),
                "announcing a subscriber failure through a subscriber is the identical hole one level up. "
                + "\"I didn't add an event\" is construction, not evidence — this is the assertion that "
                + "stops the next person adding one");
    }

    // ── one bad handler must not suppress the others ────────────────────────────────────────────────

    [Fact]
    public void AThrowingSubscriber_DoesNotSuppressTheOnesRegisteredAfterIt()
    {
        var connector = ConnectorWithAVanishedTable();
        var secondRan = false;
        connector.OnSchemaEscapeDetected += Throwing();
        connector.OnSchemaEscapeDetected += _ => secondRan = true;

        connector.SelectCount(typeof(TRow));

        secondRan.Should().BeTrue(
            "a plain handler?.Invoke(x) stops at the first delegate that throws, so a host with a logger "
            + "and a metric loses the metric to a bug in the logger — a silent drop of exactly the kind "
            + "this channel exists to prevent. GetInvocationList isolates them");
    }

    // ── the ordering, re-aimed after the first version of this test proved vacuous ──────────────────

    [Fact]
    public void ASubscriberSeesItsOwnEscape_AlreadyRecordedAndAlreadyCounted()
    {
        // ⚠ This test replaces one that asserted "the bookkeeping happens before the subscriber runs, so
        // TASK-288's heal survives a throwing handler". That claim WAS true of the unfixed code and the
        // fix made it vacuous: once RaiseDiagnostic stops the exception escaping, the increment runs
        // whether it sits before or after the raise, so moving it failed nothing. A test that cannot fail
        // is worse than no test, so it is re-aimed at the ordering that IS still observable — what a
        // handler sees of the connector at the moment it is called.
        var connector = ConnectorWithAVanishedTable();
        var generationBefore = connector.SchemaGeneration;

        var escapesVisibleToHandler = -1;
        var generationVisibleToHandler = -1L;
        connector.OnSchemaEscapeDetected += _ =>
        {
            escapesVisibleToHandler = connector.SchemaEscapes.Count;
            generationVisibleToHandler = connector.SchemaGeneration;
        };

        connector.SelectCount(typeof(TRow));

        escapesVisibleToHandler.Should().Be(1,
            "a handler that reads SchemaEscapes — the obvious thing for a host to do, since that is the "
            + "collection this channel exists to fill — must not see it empty while being told about it");
        generationVisibleToHandler.Should().BeGreaterThan(generationBefore,
            "the invalidation that heals the store is complete before any host code runs, so a handler "
            + "cannot observe a half-applied state and cannot influence whether the heal happens");
    }
    [Fact]
    public void AStoreStillHeals_EvenThoughItsHostsHandlerIsBroken()
    {
        Register();
        var store = new SQLiteStore<TRow>();
        store.SetSettings(new SqLiteSettings(_root, "throwsub.db"));
        store.Create(new TRow { Guid = Guid.NewGuid(), Name = "seed" });

        var connector = Connector();
        connector.DropTable(new[] { typeof(TRow) });
        connector.OnSchemaEscapeDetected += Throwing();

        var act = () => store.Create(new TRow { Guid = Guid.NewGuid(), Name = "w1" });
        act.Should().Throw<Exception>();

        store.Create(new TRow { Guid = Guid.NewGuid(), Name = "w2" });
        store.Count().Should().Be(1, "TASK-288 end to end, with a broken handler in the way");
    }

    // ── what must NOT change ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WithNoSubscriberAtAll_NothingIsRecordedAsAFailure()
    {
        var connector = ConnectorWithAVanishedTable();

        connector.SelectCount(typeof(TRow)).Should().Be(0);

        connector.SubscriberFailures.Should().BeEmpty(
            "the sink must not fill up in the normal case — a guard that hardened by recording everything "
            + "would pass the throwing-subscriber tests and tell an operator nothing");
        connector.SchemaEscapes.Should().ContainSingle("the escape itself is still recorded");
    }

    [Fact]
    public void AWellBehavedSubscriber_StillReceivesTheEscape_AndRecordsNoFailure()
    {
        var connector = ConnectorWithAVanishedTable();
        SchemaEscape? received = null;
        connector.OnSchemaEscapeDetected += e => received = e;

        connector.SelectCount(typeof(TRow)).Should().Be(0);

        received.Should().NotBeNull("hardening must not have turned the channel off");
        received!.TableNames.Should().Contain("ThrowSubRows");
        connector.SubscriberFailures.Should().BeEmpty();
    }
}
