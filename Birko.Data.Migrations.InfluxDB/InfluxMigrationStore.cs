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
                // Create migrations bucket with 1 year retention
                var retentionRule = new BucketRetentionRules(BucketRetentionRules.TypeEnum.Expire, 365L * 86400L);
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
            catch (global::InfluxDB.Client.Core.Exceptions.InfluxException)
            {
                // CR-L146: only swallow InfluxDB-reported failures ("bucket may not have data yet"); a
                // non-Influx exception (programming error, etc.) now propagates instead of being silently
                // eaten. Precisely distinguishing an empty bucket from an auth/connectivity InfluxException
                // needs a live server to classify (deferred to the integration tier).
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
