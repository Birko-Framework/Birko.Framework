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
/// TASK-287 — the count path <b>records</b> TASK-286's schema-escape annotation instead of discarding it
/// with the exception that carried it.
///
/// <para><b>The hole between two correct fixes.</b> TASK-285 made a <c>COUNT</c> of a missing table return
/// <b>0</b> rather than throwing; TASK-286 made <c>EnsureSchemaAndReport</c> annotate its exception when the
/// table reported missing is one this connector already created. The annotation travels <i>on the thrown
/// exception</i>, and on the count path there is no longer a thrown exception to travel on — so the one case
/// the instrument was built for was produced and immediately dropped.</para>
///
/// <para>⚠ <b>And that blind spot sat over the only shape ever seen in the wild.</b> Both occurrences
/// consumer Symbio's TASK-602 recorded were counts. Measured 2026-08-31 against a live API, both halves in
/// the same deliberately-forced condition minutes apart: <c>COUNT</c> → <c>200</c> with
/// <c>totalCount: 0</c> and <b>zero</b> log lines; write → <c>500</c> and the annotation logged. Nineteen
/// instrumented bring-ups had reported <b>0</b> escapes against <b>4,397</b> benign first-touch errors — on
/// the write path that silence is real, on the count path <c>0</c> is what a blind instrument reports
/// whether the condition happened 0 times or 19.</para>
///
/// <para>⚠ <b>The benign case is asserted as loudly as the anomalous one.</b> Ordinary lazy first-touch is
/// also a missing table and is roughly <b>245×</b> more common per bring-up, so a guard that recorded
/// everything would be indistinguishable from one that recorded nothing. The discrimination is on TASK-286's
/// annotation, never on "the table was missing".</para>
///
/// <para>⚠ <b>The write half is pinned here on purpose.</b> A test that exercises only the write path passes
/// against this defect unchanged — that is exactly how it was missed. The mutation that matters is removing
/// the recording and watching the <i>count</i> assertions fail while the write one stays green.</para>
/// </summary>
public class SchemaEscapeCountChannelTests : IDisposable
{
    private readonly string _root;

    public SchemaEscapeCountChannelTests()
        => _root = Path.Combine(Path.GetTempPath(), $"birko-escapecount-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class CRow : AbstractModel
    {
        public string? Name { get; set; }
    }

    private sealed class CRowMapping : IModelMapping<CRow>
    {
        public void Configure(ModelMap<CRow> map)
        {
            map.ToTable("EscapeCountRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(50);
        }
    }

    private SqLiteConnector Connector()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new CRowMapping());
        registry.ApplyToDatabase();
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "escapecount.db" });
        return (SqLiteConnector)factory.GetConnector();
    }

    /// <summary>
    /// The condition, forced rather than waited for: create the table so the connector records the create,
    /// then make it vanish beneath the connector.
    /// </summary>
    /// <remarks>
    /// Dropping it is a stand-in for whatever really causes the disappearance — that question is Symbio
    /// TASK-602's and stays open. The instrument must not depend on knowing which.
    /// </remarks>
    private SqLiteConnector ConnectorWithAVanishedTable()
    {
        var connector = Connector();
        connector.CreateTable(new[] { typeof(CRow) });
        connector.DropTable(new[] { typeof(CRow) });
        return connector;
    }

    // ── the defect: the anomaly is answered, and now also observed ───────────────────────────────────

    [Fact]
    public void AnAnomalousCount_StillAnswersZero_ButIsNowRECORDED()
    {
        var connector = ConnectorWithAVanishedTable();
        SchemaEscape? raised = null;
        connector.OnSchemaEscapeDetected += e => raised = e;

        var count = connector.SelectCount(typeof(CRow));

        count.Should().Be(0,
            "TASK-285's answer is unchanged — this task adds observation, not behaviour. Rethrowing here "
            + "would reopen the intermittent 500 TASK-285 closed, at roughly one bring-up in five");

        connector.SchemaEscapes.Should().ContainSingle(
            "the annotation TASK-286 produced was being discarded with the exception this path swallows, "
            + "which left the instrument blind over the only shape ever seen in the wild")
            .Which.TableNames.Should().ContainSingle().Which.Should().Be("EscapeCountRows");

        raised.Should().NotBeNull("a record nothing announces is a record nobody reads");
        raised!.Annotation.Should().Contain("but this connector already created it",
            "the record carries TASK-286's own text, so a subscriber need not walk the chain again — and "
            + "so the discriminator has one producer rather than two spellings");
        raised.Annotation.Should().Contain("EscapeCountRows created",
            "naming the earlier create with its timestamp is what separates 'created then missing' from "
            + "'never created'");
        raised.Error.Should().NotBeNull("the failure as caught is kept, provider error and all");
    }

    [Fact]
    public async Task TheASYNCCountPath_RecordsToo_BecauseItIsSeparateCode()
    {
        var connector = ConnectorWithAVanishedTable();
        SchemaEscape? raised = null;
        connector.OnSchemaEscapeDetected += e => raised = e;

        var count = await connector.SelectCountAsync(
            typeof(CRow), (IEnumerable<Birko.Data.SQL.Conditions.Condition>?)null, CancellationToken.None);

        count.Should().Be(0);
        connector.SchemaEscapes.Should().ContainSingle(
            "AbstractAsyncConnector_SelectCount is a separate code path, not a wrapper over the sync one — "
            + "shipping one of the two is how half a fix looks green");
        raised.Should().NotBeNull();
        raised!.TableNames.Should().Contain("EscapeCountRows");
    }

    // ── the benign case, which is ~245x more common and must stay silent ─────────────────────────────

    [Fact]
    public void AnORDINARYFirstTouchCount_RecordsNOTHING()
    {
        // Never created by this connector: ordinary lazy schema-ensure, not the anomaly.
        var connector = Connector();
        var raisedCount = 0;
        connector.OnSchemaEscapeDetected += _ => raisedCount++;

        var count = connector.SelectCount(typeof(CRow));

        count.Should().Be(0, "TASK-285 — unchanged for this case too");
        connector.SchemaEscapes.Should().BeEmpty(
            "measured at roughly 245 of these per Symbio bring-up against 0 anomalies in nineteen: a "
            + "channel that recorded every missing table would be no signal at all, and the guard could "
            + "then pass by recording everything");
        raisedCount.Should().Be(0);
    }

    [Fact]
    public async Task AnORDINARYFirstTouchCount_RecordsNOTHING_OnTheAsyncPathEither()
    {
        var connector = Connector();

        var count = await connector.SelectCountAsync(
            typeof(CRow), (IEnumerable<Birko.Data.SQL.Conditions.Condition>?)null, CancellationToken.None);

        count.Should().Be(0);
        connector.SchemaEscapes.Should().BeEmpty();
    }

    [Fact]
    public void ACountOfATableThatEXISTS_RecordsNothing()
    {
        var connector = Connector();
        connector.CreateTable(new[] { typeof(CRow) });

        connector.SelectCount(typeof(CRow)).Should().Be(0, "the table exists and is empty");

        connector.SchemaEscapes.Should().BeEmpty(
            "the control — without it a green result on the anomalous case is not evidence the channel is "
            + "narrow");
    }

    // ── the bookkeeping this channel inherits from the index one ─────────────────────────────────────

    [Fact]
    public void ARepeatIsKEYED_NotAppended_AndDoesNotReRaise()
    {
        var connector = ConnectorWithAVanishedTable();
        var raisedCount = 0;
        connector.OnSchemaEscapeDetected += _ => raisedCount++;

        connector.SelectCount(typeof(CRow));
        var first = connector.SchemaEscapes.Single().DetectedAt;
        Thread.Sleep(5);
        connector.SelectCount(typeof(CRow));
        connector.SelectCount(typeof(CRow));

        connector.SchemaEscapes.Should().ContainSingle(
            "connectors are cached process-wide while a web app resolves a store per request, so an "
            + "append-only list grows by one entry per request for as long as the condition lasts — the "
            + "defect TASK-204 shipped and TASK-254 extracted the fix for");
        raisedCount.Should().Be(1,
            "the event fires on the TRANSITION into the condition; the count path runs per request, so an "
            + "event per occurrence would storm exactly as the index channel's did");
        connector.SchemaEscapes.Single().DetectedAt.Should().BeAfter(first,
            "the latest occurrence overwrites, so the record describes the most recent one rather than "
            + "going stale at the first");
    }

    // ── the discriminator, and why it has to walk the chain ──────────────────────────────────────────

    [Fact]
    public void TheDiscriminator_MustWalkTheInnerChain_BecauseTheAnnotationIsWrappedAgain()
    {
        var connector = Connector();

        // EnsureSchemaAndReport rethrows as new Exception(annotatedText, ex), and a caller may wrap that
        // again — so the annotation sits at an arbitrary depth.
        var annotated = new Exception(
            "SELECT count(*) FROM \"EscapeCountRows\" [schema-ensure escape: reported missing at X, but "
            + "this connector already created it — EscapeCountRows created Y. …]",
            new Exception("SQLite Error 1: 'no such table: EscapeCountRows'."));
        var wrappedAgain = new Exception("outer", annotated);

        connector.IsAnomalousSchemaEscapeChain(wrappedAgain).Should().BeTrue(
            "a check on the outermost message compiles, runs, and silently never matches — that exact "
            + "inert guard already shipped once here, which is why IsMissingTableExceptionChain exists");
    }

    [Fact]
    public void TheDiscriminator_DoesNotMatchTheBenignAnnotation()
    {
        var connector = Connector();

        var benign = new Exception(
            "SELECT count(*) FROM \"EscapeCountRows\" [schema-ensure escape: this connector has NO "
            + "recorded CREATE TABLE for any table named in the statement]",
            new Exception("SQLite Error 1: 'no such table: EscapeCountRows'."));

        connector.IsAnomalousSchemaEscapeChain(benign).Should().BeFalse(
            "both annotations start with the same words — discriminating on 'schema-ensure escape' rather "
            + "than on the anomalous clause would record every first touch");
    }

    [Fact]
    public void TheDiscriminator_DoesNotMatchAnUnrelatedFailure()
    {
        var connector = Connector();

        var unrelated = new Exception("SELECT …", new Exception("SQLite Error 5: 'database is locked'."));

        connector.IsAnomalousSchemaEscapeChain(unrelated).Should().BeFalse();
    }

    // ── what must NOT change ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheWritePath_STILL_REPORTS_AndIsWhyThisTestFileIsNotEnoughOnItsOwn()
    {
        var connector = ConnectorWithAVanishedTable();

        var act = () => connector.Insert(new CRow { Guid = Guid.NewGuid(), Name = "x" });

        var thrown = act.Should().Throw<Exception>(
            "TASK-277 measured writes being silently discarded; the annotation reaches a log there because "
            + "the exception carries it all the way out").Which;
        thrown.Message.Should().Contain("but this connector already created it");

        connector.SchemaEscapes.Should().BeEmpty(
            "⚠ the write path deliberately does NOT feed this channel — it already reports, loudly. This "
            + "assertion is the point of the whole task: a suite exercising only writes passes against the "
            + "defect unchanged, so the mutation that proves the fix must be run against the COUNT tests");
    }
}
