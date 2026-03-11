using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using System;
using System.Threading.Tasks;

namespace Birko.Data.Migrations.InfluxDB
{
    /// <summary>
    /// Abstract base class for InfluxDB migrations.
    /// Works with InfluxDB 2.0+ using buckets and Flux.
    /// </summary>
    public abstract class InfluxMigration : Data.Migrations.AbstractMigration
    {
        /// <summary>
        /// Applies the migration using the InfluxDB client.
        /// </summary>
        /// <param name="client">The InfluxDB client.</param>
        /// <param name="organization">The organization name.</param>
        protected abstract void Up(InfluxDBClient client, string organization);

        /// <summary>
        /// Reverts the migration using the InfluxDB client.
        /// </summary>
        /// <param name="client">The InfluxDB client.</param>
        /// <param name="organization">The organization name.</param>
        protected abstract void Down(InfluxDBClient client, string organization);

        /// <summary>
        /// Throws exception - migrations require InfluxDBClient context.
        /// </summary>
        public override void Up()
        {
            throw new InvalidOperationException("InfluxMigration requires InfluxDBClient. Use InfluxMigrationRunner to execute migrations.");
        }

        /// <summary>
        /// Throws exception - migrations require InfluxDBClient context.
        /// </summary>
        public override void Down()
        {
            throw new InvalidOperationException("InfluxMigration requires InfluxDBClient. Use InfluxMigrationRunner to execute migrations.");
        }

        /// <summary>
        /// Internal execution method called by InfluxMigrationRunner.
        /// </summary>
        internal void Execute(InfluxDBClient client, string organization, Data.Migrations.MigrationDirection direction)
        {
            if (direction == Data.Migrations.MigrationDirection.Up)
            {
                Up(client, organization);
            }
            else
            {
                Down(client, organization);
            }
        }

        /// <summary>
        /// Creates a new bucket.
        /// </summary>
        protected virtual Bucket CreateBucket(InfluxDBClient client, string organization, string bucketName, int? retentionDays = null, string? description = null)
        {
            var bucketsApi = client.GetBucketsApi();

            var retentionRule = retentionDays.HasValue
                ? new BucketRetentionRules(BucketRetentionRules.TypeEnum.Expire, (long)retentionDays.Value * 86400)
                : null;

            var bucket = bucketsApi.CreateBucketAsync(bucketName, retentionRule, organization).GetAwaiter().GetResult();
            return bucket;
        }

        /// <summary>
        /// Deletes a bucket.
        /// </summary>
        protected virtual void DeleteBucket(InfluxDBClient client, string bucketNameOrId)
        {
            var bucketsApi = client.GetBucketsApi();
            var bucket = FindBucket(bucketsApi, bucketNameOrId);

            if (bucket != null)
            {
                bucketsApi.DeleteBucketAsync(bucket).GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Finds a bucket by name or ID.
        /// </summary>
        protected virtual Bucket? FindBucket(InfluxDBClient client, string bucketNameOrId)
        {
            var bucketsApi = client.GetBucketsApi();
            return FindBucket(bucketsApi, bucketNameOrId);
        }

        private Bucket? FindBucket(BucketsApi bucketsApi, string bucketNameOrId)
        {
            var buckets = bucketsApi.FindBucketsAsync().GetAwaiter().GetResult();
            return buckets.FirstOrDefault(b =>
                b.Name.Equals(bucketNameOrId, StringComparison.OrdinalIgnoreCase) ||
                b.Id.Equals(bucketNameOrId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Executes a Flux query and deletes the matching data.
        /// </summary>
        protected virtual void DeleteData(InfluxDBClient client, string organization, string bucket, string fluxPredicate)
        {
            var deleteApi = client.GetDeleteApi();
            var bucketsApi = client.GetBucketsApi();
            var bucketObj = FindBucket(bucketsApi, bucket);

            if (bucketObj != null)
            {
                var start = DateTime.UtcNow.AddYears(-100);
                var stop = DateTime.UtcNow;
                deleteApi.Delete(start, stop, fluxPredicate, bucket, bucketObj.OrgID);
            }
        }

        /// <summary>
        /// Creates a bucket with retention period.
        /// </summary>
        protected virtual Bucket CreateBucketWithRetention(InfluxDBClient client, string organization, string bucketName, int retentionHours)
        {
            var bucketsApi = client.GetBucketsApi();
            var retentionRule = new BucketRetentionRules(BucketRetentionRules.TypeEnum.Expire, (long)retentionHours * 3600);
            return bucketsApi.CreateBucketAsync(bucketName, retentionRule, organization).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Checks if a bucket exists.
        /// </summary>
        protected virtual bool BucketExists(InfluxDBClient client, string bucketName)
        {
            return FindBucket(client, bucketName) != null;
        }
    }
}
