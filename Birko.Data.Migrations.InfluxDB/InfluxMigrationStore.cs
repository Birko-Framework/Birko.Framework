using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using System;
using System.Collections.Generic;
using System.Linq;
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
        public Task InitializeAsync()
        {
            Initialize();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets all applied migration versions.
        /// </summary>
        public ISet<long> GetAppliedVersions()
        {
            if (_migrationsBucket == null)
            {
                Initialize();
            }

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
            catch
            {
                // Bucket may not have data yet
            }

            return result;
        }

        /// <summary>
        /// Asynchronously gets all applied migration versions.
        /// </summary>
        public Task<ISet<long>> GetAppliedVersionsAsync()
        {
            return Task.FromResult(GetAppliedVersions());
        }

        /// <summary>
        /// Records that a migration has been applied.
        /// </summary>
        public void RecordMigration(Data.Migrations.IMigration migration)
        {
            if (_migrationsBucket == null)
            {
                Initialize();
            }

            var writeApi = _client.GetWriteApi();
            var point = PointData.Measurement(MigrationMeasurement)
                .Tag("name", migration.Name)
                .Field("version", migration.Version)
                .Field("description", migration.Description ?? "")
                .Timestamp(migration.CreatedAt, WritePrecision.Ms);

            writeApi.WritePoint(point, MigrationsBucketName, _organization);
            writeApi.Flush();
        }

        /// <summary>
        /// Asynchronously records that a migration has been applied.
        /// </summary>
        public Task RecordMigrationAsync(Data.Migrations.IMigration migration)
        {
            RecordMigration(migration);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Removes a migration record (when downgrading).
        /// </summary>
        public void RemoveMigration(Data.Migrations.IMigration migration)
        {
            if (_migrationsBucket == null)
            {
                Initialize();
            }

            var deleteApi = _client.GetDeleteApi();
            var fluxPredicate = $"_measurement=\"{MigrationMeasurement}\" AND name=\"{migration.Name}\"";

            var start = migration.CreatedAt.AddMinutes(-1);
            var stop = DateTime.UtcNow;

            try
            {
                deleteApi.Delete(start, stop, fluxPredicate, MigrationsBucketName, _migrationsBucket!.OrgID);
            }
            catch
            {
                // Ignore if already deleted
            }
        }

        /// <summary>
        /// Asynchronously removes a migration record.
        /// </summary>
        public Task RemoveMigrationAsync(Data.Migrations.IMigration migration)
        {
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
        public Task<long> GetCurrentVersionAsync()
        {
            return Task.FromResult(GetCurrentVersion());
        }
    }
}
