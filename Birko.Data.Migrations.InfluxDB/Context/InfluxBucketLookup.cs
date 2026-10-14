using System;
using System.Linq;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Core.Exceptions;

namespace Birko.Data.Migrations.InfluxDB.Context
{
    /// <summary>
    /// Finds one bucket of one organization by its exact name. The one lookup every migration component uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TASK-612. InfluxDB 2 bucket names are case-sensitive (measured: <c>metrics</c> and <c>Metrics</c> coexist, and
    /// <c>?name=Metrics</c> returns only <c>Metrics</c>). The lookups this replaces listed buckets and matched names
    /// case-insensitively, across every organization the token can see. So <c>DropCollection("Metrics")</c> deleted
    /// <c>metrics</c>, and a bucket of the same name in another organization counted as this migration's. The server
    /// filters instead: <c>org</c> plus <c>name</c>, compared again exactly here.
    /// </para>
    /// <para>
    /// ⚠ A name filter that matches nothing is answered with <b>404</b>, not an empty list, which is why
    /// <see cref="NotFoundException"/> means "absent" here and nowhere else.
    /// </para>
    /// </remarks>
    internal static class InfluxBucketLookup
    {
        public static Bucket? Find(InfluxDBClient client, string organization, string name)
        {
            try
            {
                return client.GetBucketsApi().FindBucketsAsync(org: organization, name: name).GetAwaiter().GetResult()
                    .FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.Ordinal));
            }
            catch (NotFoundException)
            {
                return null;
            }
        }

        /// <summary>
        /// Creates <paramref name="name"/> in the organization NAMED <paramref name="organization"/>.
        /// </summary>
        /// <remarks>
        /// The <c>CreateBucketAsync(string, rules, string)</c> overload takes an organization ID, and these components
        /// are given a name (as the data store's <c>Settings.Organization</c> is). Passing the name was a 400, "id must
        /// have a length of 16 bytes" (measured), so no migration bucket could be created. The name is resolved here.
        /// </remarks>
        public static Bucket Create(InfluxDBClient client, string organization, string name, BucketRetentionRules? retention)
        {
            var org = client.GetOrganizationsApi().FindOrganizationsAsync(org: organization).GetAwaiter().GetResult()
                .FirstOrDefault(o => string.Equals(o.Name, organization, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"InfluxDB organization '{organization}' does not exist.");
            return client.GetBucketsApi().CreateBucketAsync(name, retention, org).GetAwaiter().GetResult();
        }
    }
}
