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
        private const string MigrationsBucketName = "_migrations";
        private const string MigrationMeasurement = "migrations";

        private readonly InfluxDBClient _client;
        private readonly string _organization;
        private Bucket? _migrationsBucket;

        /// <summary>
        /// Initializes a new instance of the InfluxMigrationStore class.
        /// </summary>
        public InfluxMigrationStore(InfluxDBClient client, string organization)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _organization = organization ?? throw new ArgumentNullException(nameof(organization));
        }

        /// <summary>
        /// Initializes the migration store (creates migrations bucket if needed).
        /// </summary>
        public void Initialize()
        {
            var bucketsApi = _client.GetBucketsApi();
            var buckets = bucketsApi.FindBucketsAsync().GetAwaiter().GetResult();

            _migrationsBucket = buckets.FirstOrDefault(b =>
                b.Name.Equals(MigrationsBucketName, StringComparison.OrdinalIgnoreCase));

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
                // method CREATES -- an existing `_migrations` bucket is adopted as found, so a database
                // provisioned before this fix keeps its 365-day rule and needs a manual
                // `influx bucket update --name _migrations --retention 0`.
                var retentionRule = new BucketRetentionRules(BucketRetentionRules.TypeEnum.Expire, 0L);
                _migrationsBucket = bucketsApi.CreateBucketAsync(MigrationsBucketName, retentionRule, _organization).GetAwaiter().GetResult();
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
            var query = $@"
                from(bucket: ""{MigrationsBucketName}"")
                |> range(start: -10y)
                |> filter(fn: (r) => r._measurement == ""{MigrationMeasurement}"")
                |> filter(fn: (r) => r._field == ""version"")
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
                    $"Cannot read applied migration versions from bucket '{MigrationsBucketName}'. "
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
            var point = PointData.Measurement(MigrationMeasurement)
                .Tag("name", migration.Name)
                .Field("version", migration.Version)
                .Field("description", migration.Description ?? "")
                .Timestamp(migration.CreatedAt, WritePrecision.Ms);

            writeApi.WritePointAsync(point, MigrationsBucketName, _organization).GetAwaiter().GetResult();
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

            var deleteApi = _client.GetDeleteApi();
            // CR-M111: escape the interpolated name so a value containing a quote/backslash can't break
            // (or alter) the delete predicate.
            var fluxPredicate = $"_measurement=\"{MigrationMeasurement}\" AND name=\"{EscapeFluxString(migration.Name)}\"";

            var start = migration.CreatedAt.AddMinutes(-1);
            var stop = DateTime.UtcNow;

            try
            {
                deleteApi.Delete(start, stop, fluxPredicate, MigrationsBucketName, _migrationsBucket!.OrgID);
            }
            catch (global::InfluxDB.Client.Core.Exceptions.InfluxException)
            {
                // CR-L146: only swallow InfluxDB-reported delete failures ("already deleted"); a non-Influx
                // exception now propagates. Distinguishing already-deleted from a genuine delete failure
                // within InfluxException needs a live server (deferred to the integration tier).
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
        internal static string EscapeFluxString(string? value)
            => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
