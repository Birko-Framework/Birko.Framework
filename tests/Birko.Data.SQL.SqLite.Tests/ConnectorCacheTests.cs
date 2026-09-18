using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests
{
    /// <summary>
    /// TASK-276 — pins what <c>DataBase.GetConnector</c> actually shares, because every hypothesis about the
    /// rare cross-class failures in this family rests on it and nothing asserted it.
    ///
    /// <para>
    /// <b>Why it lives here rather than in <c>Birko.Data.SQL.Tests</c>,</b> which declares
    /// <c>DataBase.GetConnector</c>: the cache can only be exercised through a concrete connector, and
    /// <c>Activator.CreateInstance</c> needs one whose constructor takes <c>Settings</c>. The abstract project
    /// has no such type, so § Testing's "test a member in its declaring project" gives way to being able to
    /// test it at all.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Connectors are cached process-wide in a <c>ConcurrentDictionary</c> keyed by
    /// <c>(connector type, settings.GetId())</c>, and <c>Settings.GetId()</c> is <c>"{Location}:{Name}"</c>.
    /// Several suites already <i>depend</i> on that keying in prose — "a fresh database file per test, so the
    /// process-wide connector cache hands out a distinct connector per test" — while nothing checked it. A
    /// shared object that tests reason about but never assert is how TASK-240 (the ambient boundary),
    /// TASK-259 (a disposed connection left on a cached connector) and the index-failure list that grew per
    /// request all happened.
    /// </para>
    /// <para>
    /// <b>The third test is the sharp edge.</b> Two settings objects that differ in everything except
    /// <c>Location</c> and <c>Name</c> share one connector — so the second caller's timeout, retry policy and
    /// every other setting are silently discarded in favour of whoever constructed it first.
    /// <see cref="Birko.Data.SQL.DataBase.GetConnector{T}"/> is the subject of TASK-270; this records the
    /// behaviour as it is, so that task starts from a measurement rather than a reading.
    /// </para>
    /// </remarks>
    public class ConnectorCacheTests
    {
        private static SqLiteSettings Settings(string location, string name, int timeout = 30)
            => new(location, name) { CommandTimeout = timeout };

        [Fact]
        public void The_same_settings_id_yields_the_same_connector_instance()
        {
            var first = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(Settings("/cache-a", "one.db"));
            var second = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(Settings("/cache-a", "one.db"));

            second.Should().BeSameAs(first,
                "the cache is keyed by (type, settings id) and is process-wide — this is what makes any "
              + "per-caller state on a connector a correctness bug rather than a style one");
        }

        [Fact]
        public void A_different_settings_id_yields_a_different_connector()
        {
            var a = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(Settings("/cache-b", "one.db"));
            var b = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(Settings("/cache-b", "two.db"));
            var c = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(Settings("/cache-c", "one.db"));

            b.Should().NotBeSameAs(a, "the file name is part of the id");
            c.Should().NotBeSameAs(a, "so is the location — which is why a per-test temp directory isolates");
        }

        /// <summary>
        /// ⚠ The consequence worth knowing: the id is <c>Location:Name</c> and <b>nothing else</b>, so every
        /// other setting on the second caller's object is discarded.
        /// </summary>
        [Fact]
        public void Settings_that_differ_outside_location_and_name_still_share_one_connector()
        {
            var first = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(Settings("/cache-d", "one.db", timeout: 3));
            var second = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(Settings("/cache-d", "one.db", timeout: 90));

            second.Should().BeSameAs(first);
            ((SqLiteSettings)second.Settings).CommandTimeout.Should().Be(3,
                "the FIRST caller's settings win for everyone — the second caller's CommandTimeout is "
              + "silently ignored, which is TASK-270's subject");
        }

        [Fact]
        public void The_sync_and_async_caches_are_separate()
        {
            var sync = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(Settings("/cache-e", "one.db"));
            var async = Birko.Data.SQL.DataBase.GetAsyncConnector<SqLiteConnector>(Settings("/cache-e", "one.db"));

            // SqLiteConnector IS the async connector — it derives from AbstractAsyncConnector — so the two
            // caches hand out two DIFFERENT instances of the SAME type for one database.
            ((object)async).Should().NotBeSameAs(sync,
                "two dictionaries, so a sync and an async store on one database hold different connector "
              + "instances — which is why AmbientSqlTransaction is keyed by settings id rather than by "
              + "connector instance, and why per-connector state is shared unpredictably");
        }
    }
}
