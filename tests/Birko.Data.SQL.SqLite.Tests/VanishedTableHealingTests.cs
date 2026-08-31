using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-288 — a store whose table vanishes beneath it <b>heals on the next operation</b>, and every heal
/// is recorded.
///
/// <para><b>The contract that was false.</b> <c>EnsureSchemaAndReport</c> documented itself, and TASK-277
/// justified it, as <i>"rethrow so this attempt is reported, but call <c>DoInit()</c> so the next attempt
/// can succeed"</i>. The first half held; the second did not. <c>DoInit()</c> raises <c>OnInit</c>, which
/// nothing in the framework subscribes to, and it issues no per-entity DDL — so nothing ever re-created
/// the table, because the store's remembered <c>_initialized</c> short-circuited
/// <c>EnsureInitializedAsync</c> before <c>InitCore</c> could run again.</para>
///
/// <para><b>Measured before the fix</b> (this file's probe, and consumer Symbio's TASK-627 against a live
/// API): with the table dropped beneath an initialised store, <b>five consecutive writes threw and
/// <c>sqlite_master</c> held 0 rows throughout</b>; only a fresh store instance — a process restart, in
/// miniature — recovered it. That is what proves the broken state was in memory, not on disk.</para>
///
/// <para>⚠ <b>Reads hid it, which is what made it dangerous.</b> The same dropped table answered a count
/// with <b>0 and no error</b> (TASK-211/TASK-285, both correct), so the surface looked healthy while every
/// write failed and a read-based monitor saw nothing. That asymmetry is asserted here so nobody "fixes"
/// one side of it in isolation.</para>
///
/// <para>⚠ <b>Healing was a decision with a consequence, taken deliberately.</b> Symbio TASK-602 was
/// reasoning from "a real absence never heals, and both observed occurrences healed, therefore the anomaly
/// is not an absent table" — healing destroys that discriminator. It is only acceptable because the heal
/// now announces itself on <c>SchemaEscapes</c> / <c>OnSchemaEscapeDetected</c>, which is a stronger signal
/// than the one it took away: an absent table is recorded rather than inferred. The recording is therefore
/// not a nicety here, it is the precondition — which is why it has its own assertions below.</para>
/// </summary>
public class VanishedTableHealingTests : IDisposable
{
    private readonly string _root;

    public VanishedTableHealingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-heal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class HRow : AbstractModel
    {
        public string? Name { get; set; }
    }

    private sealed class HRowMapping : IModelMapping<HRow>
    {
        public void Configure(ModelMap<HRow> map)
        {
            map.ToTable("HealRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(50);
        }
    }

    private void Register()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new HRowMapping());
        registry.ApplyToDatabase();
    }

    private SqLiteConnector Connector()
        => (SqLiteConnector)new SqLiteStoreFactory(
            new SqLiteStoreFactoryOptions { Location = _root, Name = "heal.db" }).GetConnector();

    /// <summary>
    /// Reads <c>sqlite_master</c> on a connection of its own — the store's own answer is exactly what is
    /// under test, so it cannot also be the measurement.
    /// </summary>
    private int TablesNamedHealRows()
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(_root, "heal.db")}");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='HealRows'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ── the defect ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AVanishedTable_IsRecreatedOnTheNextWrite_NotAfterARestart()
    {
        Register();
        var store = new AsyncSQLiteStore<HRow>();
        store.SetSettings(new SqLiteSettings(_root, "heal.db"));
        await store.CreateAsync(new HRow { Guid = Guid.NewGuid(), Name = "seed" });
        TablesNamedHealRows().Should().Be(1);

        // The table vanishes beneath the running store: _initialized stays true while the table is gone.
        // Dropping it stands in for whatever really causes the disappearance — Symbio TASK-602's question,
        // still open. The heal must not depend on knowing which.
        Connector().DropTable(new[] { typeof(HRow) });
        TablesNamedHealRows().Should().Be(0);

        var first = await Attempt(store, "w1");
        first.Should().BeFalse(
            "this attempt is still REPORTED — TASK-277 measured writes being silently discarded, and "
            + "healing must not buy recovery back by going quiet about the operation that was lost");
        TablesNamedHealRows().Should().Be(0,
            "nothing recreates the table during the failing attempt itself; the schema-ensure happens on "
            + "the next one, which is what 'so the next attempt can succeed' actually means");

        var second = await Attempt(store, "w2");
        second.Should().BeTrue(
            "before TASK-288 this threw, and so did writes 3, 4 and 5, with sqlite_master at 0 rows "
            + "throughout — the entity was write-broken for the life of the process and only a restart "
            + "recovered it");
        TablesNamedHealRows().Should().Be(1);

        (await Attempt(store, "w3")).Should().BeTrue();
        (await store.CountAsync()).Should().Be(2, "w2 and w3 — the seed row went with the dropped table");
    }

    [Fact]
    public void TheSyncStoreHealsToo_BecauseItIsASeparateGate()
    {
        Register();
        var store = new SQLiteStore<HRow>();
        store.SetSettings(new SqLiteSettings(_root, "heal.db"));
        store.Create(new HRow { Guid = Guid.NewGuid(), Name = "seed" });
        Connector().DropTable(new[] { typeof(HRow) });

        var act = () => store.Create(new HRow { Guid = Guid.NewGuid(), Name = "w1" });
        act.Should().Throw<Exception>();

        store.Create(new HRow { Guid = Guid.NewGuid(), Name = "w2" });
        TablesNamedHealRows().Should().Be(1,
            "AbstractStore and AbstractAsyncStore keep their own _initialized and their own double-checked "
            + "gate; a fix applied to one of the two is how half of this looks green");
    }

    // ── the heal must be observable, which is the whole licence for healing at all ───────────────────

    [Fact]
    public async Task EveryHealIsRECORDED_SoTheAbsenceIsNoLongerOnlyInferable()
    {
        Register();
        var connector = Connector();
        SchemaEscape? raised = null;
        connector.OnSchemaEscapeDetected += e => raised = e;

        var store = new AsyncSQLiteStore<HRow>();
        store.SetSettings(new SqLiteSettings(_root, "heal.db"));
        await store.CreateAsync(new HRow { Guid = Guid.NewGuid(), Name = "seed" });
        connector.DropTable(new[] { typeof(HRow) });

        await Attempt(store, "w1");

        connector.SchemaEscapes.Should().ContainSingle(
            "healing removes Symbio TASK-602's discriminator — 'a real absence never heals' — so it is "
            + "only acceptable because the absence now announces itself instead of being inferred from a "
            + "symptom. Without this the behaviour change is a net loss of information")
            .Which.TableNames.Should().Contain("HealRows");
        raised.Should().NotBeNull();
        raised!.Annotation.Should().Contain("but this connector already created it");
    }

    [Fact]
    public void TheGenerationMovesOnlyForTheANOMALY_NotForAnOrdinaryFirstTouch()
    {
        Register();
        var connector = Connector();
        var before = connector.SchemaGeneration;

        // A statement against a table this connector never created: ordinary lazy first-touch, and the
        // failure shape a Symbio bring-up produces roughly 245 times.
        connector.SelectCount(typeof(HRow)).Should().Be(0);

        connector.SchemaGeneration.Should().Be(before,
            "reacting to any missing table would invalidate every store on the database hundreds of times "
            + "per start-up, for a condition that is not the anomaly and needs no healing");
        connector.SchemaEscapes.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnaffectedStore_DoesNotReInitialiseOnEveryOperation()
    {
        Register();
        var store = new AsyncSQLiteStore<HRow>();
        store.SetSettings(new SqLiteSettings(_root, "heal.db"));
        await store.CreateAsync(new HRow { Guid = Guid.NewGuid(), Name = "seed" });

        var connector = Connector();
        var generation = connector.SchemaGeneration;

        for (var i = 0; i < 5; i++)
        {
            await store.CreateAsync(new HRow { Guid = Guid.NewGuid(), Name = $"n{i}" });
        }

        connector.SchemaGeneration.Should().Be(generation,
            "the steady state must cost nothing — this hook is read on every operation, so a generation "
            + "that drifted would turn every CRUD call into a schema-ensure");
        (await store.CountAsync()).Should().Be(6);
    }

    // ── the asymmetry that hid it, pinned so neither side is 'fixed' alone ───────────────────────────

    [Fact]
    public async Task AReadOfTheVanishedTable_STILL_AnswersZeroWithoutError()
    {
        Register();
        var store = new AsyncSQLiteStore<HRow>();
        store.SetSettings(new SqLiteSettings(_root, "heal.db"));
        await store.CreateAsync(new HRow { Guid = Guid.NewGuid(), Name = "seed" });
        Connector().DropTable(new[] { typeof(HRow) });

        var count = await store.CountAsync();

        count.Should().Be(0,
            "TASK-285's answer is unchanged. It is also why this defect was quiet: a monitor watching "
            + "reads sees a healthy surface while every write fails");
    }

    private static async Task<bool> Attempt(AsyncSQLiteStore<HRow> store, string name)
    {
        try
        {
            await store.CreateAsync(new HRow { Guid = Guid.NewGuid(), Name = name });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
