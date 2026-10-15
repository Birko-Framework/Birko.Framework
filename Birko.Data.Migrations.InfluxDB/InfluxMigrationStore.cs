using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Migrations.InfluxDB
{
    /// <summary>
    /// Stores migration state in an InfluxDB bucket.
    /// </summary>
    public class InfluxMigrationStore : Data.Migrations.IMigrationStore
    {
        /// <summary>
        /// The bucket migration bookkeeping lives in unless the caller names another.
        /// </summary>
        /// <remarks>
        /// TASK-617. This was <c>_migrations</c>, and InfluxDB 2 refuses it (measured: "bucket name _migrations is
        /// invalid. Buckets may not start with underscore", the prefix being reserved for system buckets). So
        /// <see cref="Initialize"/> could never create its bucket, and no migration could run on a fresh server.
        /// There is deliberately no fallback that adopts an old <c>_migrations</c> bucket: the API refuses to create
        /// one and a 1.x upgrade names buckets <c>database/retention-policy</c>, so no database can have it, and a
        /// branch nothing can reach would only drift (rule 53).
        /// </remarks>
        public const string DefaultMigrationsBucketName = "birko_migrations";

        private const string MigrationMeasurement = "migrations";

        private readonly InfluxDBClient _client;
        private readonly string _organization;
        private readonly string _bucketName;
        private Bucket? _migrationsBucket;

        /// <summary>
        /// Initializes a new instance of the InfluxMigrationStore class.
        /// </summary>
        /// <param name="client">The InfluxDB client.</param>
        /// <param name="organization">The organization NAME.</param>
        /// <param name="bucketName">The bookkeeping bucket; defaults to <see cref="DefaultMigrationsBucketName"/>.</param>
        /// <exception cref="ArgumentException">The bucket name is blank or starts with '_', which InfluxDB 2 refuses.</exception>
        public InfluxMigrationStore(InfluxDBClient client, string organization, string? bucketName = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _organization = organization ?? throw new ArgumentNullException(nameof(organization));
            _bucketName = bucketName ?? DefaultMigrationsBucketName;
            if (string.IsNullOrWhiteSpace(_bucketName) || _bucketName.StartsWith("_", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Migration bucket name '{_bucketName}' is not one InfluxDB 2 accepts: it must be non-blank and must not "
                    + "start with '_' (reserved for system buckets).",
                    nameof(bucketName));
            }
        }

        /// <summary>
        /// The bucket bookkeeping is read from and written to.
        /// </summary>
        public string BucketName => _bucketName;

        /// <summary>
        /// Initializes the migration store (creates migrations bucket if needed).
        /// </summary>
        public void Initialize()
        {
            _migrationsBucket = Context.InfluxBucketLookup.Find(_client, _organization, _bucketName);

            if (_migrationsBucket == null)
            {
                // SH-H033: migration bookkeeping must NEVER expire. This used to create the bucket with a
                // 365-day Expire rule, and RecordMigration timestamps each point with `migration.CreatedAt`
                // -- the migration's *authored* date, not when it was applied. So the defect is not only
                // "applied versions vanish after a year": a migration authored more than a year ago falls
                // outside the retention window the moment it is written and is never durably recorded at
                // all. Either way GetAppliedVersions() comes back short, GetCurrentVersion() under-reports,
                // and the next Migrate() replays migrations (including destructive Up bodies) against an
                // already-migrated database.
                //
                // `0` is InfluxDB's spelling for infinite retention. Note this only governs a bucket this
                // method CREATES -- an existing bookkeeping bucket is adopted as found, so a database
                // provisioned before this fix keeps its retention rule and needs a manual
                // `influx bucket update --name <bucket> --retention 0`.
                var retentionRule = new BucketRetentionRules(BucketRetentionRules.TypeEnum.Expire, 0L);
                _migrationsBucket = Context.InfluxBucketLookup.Create(_client, _organization, _bucketName, retentionRule);
            }
        }

        /// <summary>
        /// Asynchronously initializes the migration store.
        /// </summary>
        /// <remarks>
        /// CR-L145: the <c>*Async</c> members observe the <see cref="CancellationToken"/> at entry via
        /// <c>ThrowIfCancellationRequested</c>, but do not yet thread it into the InfluxDB SDK's async
        /// calls — the bodies run the synchronous store methods. Genuine SDK-async cancellation is the
        /// deferred CR-M108 work (needs a live InfluxDB to verify).
        /// </remarks>
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Initialize();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Lazily initializes the store (CR-L147: single helper replacing the duplicated
        /// <c>if (_migrationsBucket == null) Initialize();</c> blocks). Not double-checked-locked — the
        /// migration runner drives this single-threaded.
        /// </summary>
        private void EnsureInitialized()
        {
            if (_migrationsBucket == null)
            {
                Initialize();
            }
        }

        /// <summary>
        /// Gets all applied migration versions.
        /// </summary>
        public ISet<long> GetAppliedVersions()
        {
            EnsureInitialized();

            var queryApi = _client.GetQueryApi();
            // TASK-152: from the epoch. A version point carries its migration's CreatedAt, and `-10y` made a migration
            // created more than ten years ago (a fixed historical date) read as never applied, so it was replayed.
            var query = $@"
                from(bucket: ""{EscapeFluxString(BucketName)}"")
                |> range(start: 0)
                |> filter(fn: (r) => r._measurement == ""{MigrationMeasurement}"")
                |> filter(fn: (r) => r._field == ""version"")
                |> group()
                |> distinct(column: ""_value"")
            ";

            var result = new HashSet<long>();
            try
            {
                var tables = queryApi.QueryAsync(query, _organization).GetAwaiter().GetResult();
                foreach (var table in tables)
                {
                    foreach (var record in table.Records)
                    {
                        var recordValue = record.GetValueByKey("_value");
                        if (recordValue != null && long.TryParse(recordValue.ToString(), out var version))
                        {
                            result.Add(version);
                        }
                    }
                }
            }
            catch (global::InfluxDB.Client.Core.Exceptions.InfluxException ex)
            {
                // SH-H032's sibling, SH-H029/SH-H030: "could not read" is never "nothing is applied".
                //
                // CR-L146 narrowed this catch to InfluxException and recorded that it still could not
                // separate an empty bucket from an auth / wrong-organization / connectivity failure. That
                // remains true -- and swallowing is the wrong side of the ambiguity, because the empty set
                // flows into GetCurrentVersion() == 0 and Migrate() then REPLAYS every registered migration
                // (including any destructive Up) against a live, fully-migrated database. A spurious throw
                // costs one failed run that says why; a spurious empty set costs the database.
                //
                // A genuinely empty bucket does not raise: Influx answers an empty result set, which the
                // loop above handles by yielding no records. So the only reachable causes here are real
                // failures. The status code is carried through so a caller can still classify it.
                throw new InvalidOperationException(
                    $"Cannot read applied migration versions from bucket '{BucketName}'. "
                    + "Refusing to report an empty set, which would replay every registered migration "
                    + $"against an already-migrated database. {ex.Message}",
                    ex);
            }

            return result;
        }

        /// <summary>
        /// Asynchronously gets all applied migration versions.
        /// </summary>
        public Task<ISet<long>> GetAppliedVersionsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(GetAppliedVersions());
        }

        /// <summary>
        /// Records that a migration has been applied.
        /// </summary>
        public void RecordMigration(Data.Migrations.IMigration migration)
        {
            EnsureInitialized();

            // Use the synchronous WriteApiAsync (writes immediately, no background worker) instead of
            // the batching GetWriteApi() — the latter is IDisposable, owns a background thread, was
            // never disposed (leak) and its async Flush() didn't guarantee commit before a later read
            // (CR-H060). Mirrors AsyncInfluxDBStore.
            var writeApi = _client.GetWriteApiAsync();
            // SH-M189 (TASK-152): the version is a TAG as well as a field. A point's identity is measurement + tags + time,
            // and with the name as the only tag two migrations sharing a name and a CreatedAt millisecond overwrote each
            // other, so one applied version vanished.
            var point = PointData.Measurement(MigrationMeasurement)
                .Tag("name", migration.Name)
                .Tag("version", migration.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Field("version", migration.Version)
                .Field("description", migration.Description ?? "")
                .Timestamp(migration.CreatedAt, WritePrecision.Ms);

            writeApi.WritePointAsync(point, BucketName, _organization).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously records that a migration has been applied.
        /// </summary>
        public Task RecordMigrationAsync(Data.Migrations.IMigration migration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecordMigration(migration);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Removes a migration record (when downgrading).
        /// </summary>
        public void RemoveMigration(Data.Migrations.IMigration migration)
        {
            EnsureInitialized();

            // SH-M188 (TASK-152): the delete window used to start at migration.CreatedAt - 1 minute, but CreatedAt is set
            // when the migration object is constructed, so in a later process (the usual rollback) it is "now" and the
            // window missed the point written days earlier: nothing deleted, success reported, and Rollback could never
            // pass that version. SH-M189: the predicate was the name alone, deleting every version sharing it. The
            // version's own points are looked up instead, and each is deleted at its exact timestamp by its own tags.
            var deleteApi = _client.GetDeleteApi();
            foreach (var (time, name, versionTagged) in FindVersionPoints(migration.Version))
            {
                // CR-M111: escaped. TASK-618: tag keys quoted (`name` is a keyword in the delete-predicate grammar).
                var predicate = $"_measurement=\"{MigrationMeasurement}\" AND \"name\"=\"{EscapeFluxString(name)}\""
                    + (versionTagged ? $" AND \"version\"=\"{migration.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"" : "");

                // Waited on, and not caught: a refused delete propagates rather than reporting a downgrade that left the
                // migration recorded (rule 22; measured at TASK-618, a predicate matching nothing is a 204).
                deleteApi.Delete(time, time.AddMilliseconds(1), predicate, BucketName, _migrationsBucket!.OrgID).GetAwaiter().GetResult();
            }

            if (FindVersionPoints(migration.Version).Count > 0)
            {
                throw new InvalidOperationException(
                    $"Migration {migration.Version} ({migration.Name}) is still recorded in bucket '{BucketName}' after its delete.");
            }
        }

        /// <summary>
        /// Asynchronously removes a migration record.
        /// </summary>
        public Task RemoveMigrationAsync(Data.Migrations.IMigration migration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveMigration(migration);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets the current version of the database.
        /// </summary>
        public long GetCurrentVersion()
        {
            var versions = GetAppliedVersions();
            return versions.Any() ? versions.Max() : 0;
        }

        /// <summary>
        /// Asynchronously gets the current version.
        /// </summary>
        public Task<long> GetCurrentVersionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(GetCurrentVersion());
        }

        /// <summary>
        /// Escapes a value for safe interpolation into a double-quoted Flux string literal (CR-M111):
        /// backslashes and embedded double quotes are backslash-escaped.
        /// </summary>
        /// <summary>The points recording <paramref name="version"/>: their time, name tag, and whether they carry a version tag.</summary>
        private List<(DateTime Time, string Name, bool VersionTagged)> FindVersionPoints(long version)
        {
            var query = $@"
                from(bucket: ""{EscapeFluxString(BucketName)}"")
                |> range(start: 0)
                |> filter(fn: (r) => r._measurement == ""{MigrationMeasurement}"" and r._field == ""version"" and r._value == {version.ToString(System.Globalization.CultureInfo.InvariantCulture)})
            ";
            var points = new List<(DateTime, string, bool)>();
            foreach (var table in _client.GetQueryApi().QueryAsync(query, _organization).GetAwaiter().GetResult())
            {
                foreach (var record in table.Records)
                {
                    if (record.GetTimeInDateTime() is not DateTime time) continue;
                    var name = record.GetValueByKey("name")?.ToString() ?? string.Empty;
                    points.Add((DateTime.SpecifyKind(time, DateTimeKind.Utc), name, record.Values.ContainsKey("version")));
                }
            }
            return points;
        }

        internal static string EscapeFluxString(string? value)
            => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
