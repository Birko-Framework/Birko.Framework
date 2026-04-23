using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Migrations.Context;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;

namespace Birko.Data.Migrations.InfluxDB.Context
{
    public class InfluxDBDataMigrator : IDataMigrator
    {
        private readonly InfluxDBClient _client;
        private readonly string _organization;

        public InfluxDBDataMigrator(InfluxDBClient client, string organization)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _organization = organization ?? throw new ArgumentNullException(nameof(organization));
        }

        public void UpdateDocuments(string collection, string filterJson, IDictionary<string, object> updates)
        {
            // InfluxDB is immutable — updates are not directly supported.
            // The typical pattern is to delete and rewrite the data.
            // Use Raw() for advanced data manipulation.
        }

        public void DeleteDocuments(string collection, string filterJson)
        {
            var deleteApi = _client.GetDeleteApi();
            var bucketsApi = _client.GetBucketsApi();

            var buckets = bucketsApi.FindBucketsAsync().GetAwaiter().GetResult();
            var bucket = buckets.FirstOrDefault(b =>
                b.Name.Equals(collection, StringComparison.OrdinalIgnoreCase));

            if (bucket != null)
            {
                var start = DateTime.UtcNow.AddYears(-100);
                var stop = DateTime.UtcNow;

                // Use filterJson as the Flux predicate if provided
                var predicate = string.IsNullOrWhiteSpace(filterJson) || filterJson.Trim() == "{}"
                    ? ""
                    : ConvertFilterToFluxPredicate(filterJson);

                deleteApi.Delete(start, stop, predicate, collection, bucket.OrgID);
            }
        }

        public long CountDocuments(string collection, string? filterJson = null)
        {
            var queryApi = _client.GetQueryApi();

            var flux = $"from(bucket: \"{collection}\")"
                + " |> range(start: -100y)"
                + " |> filter(fn: (r) => true)"
                + " |> count()"
                + " |> group()"
                + " |> sum()";

            var tables = queryApi.QueryAsync(flux, _organization).GetAwaiter().GetResult();

            long total = 0;
            foreach (var table in tables)
            {
                foreach (var record in table.Records)
                {
                    var value = record.GetValue();
                    if (value is long l)
                        total += l;
                    else if (value is int i)
                        total += i;
                    else if (value is double d)
                        total += (long)d;
                }
            }

            return total;
        }

        public void CopyData(string sourceCollection, string targetCollection, string? transformJson = null)
        {
            // InfluxDB copy requires reading from source and writing to target.
            // Use Flux to query source, then write to target.
            var queryApi = _client.GetQueryApi();
            var writeApi = _client.GetWriteApi();

            var flux = $"from(bucket: \"{sourceCollection}\")"
                + " |> range(start: -100y)"
                + " |> filter(fn: (r) => true)";

            var tables = queryApi.QueryAsync(flux, _organization).GetAwaiter().GetResult();

            foreach (var table in tables)
            {
                foreach (var record in table.Records)
                {
                    var point = PointData.Measurement(record.GetMeasurement())
                        .Tag("_original_bucket", sourceCollection);

                    // Copy all fields from the record
                    foreach (var entry in record.Values)
                    {
                        var key = entry.Key;
                        if (key == "_measurement" || key == "_time" || key == "_start" || key == "_stop")
                            continue;

                        if (entry.Value is string strVal)
                            point = point.Tag(key, strVal);
                        else if (entry.Value != null)
                            point = point.Field(key, Convert.ToDouble(entry.Value));
                    }

                    writeApi.WritePoint(point, targetCollection, _organization);
                }
            }

            writeApi.Flush();
        }

        public void BulkInsert(string collection, IEnumerable<IDictionary<string, object>> documents)
        {
            if (documents == null) return;

            var writeApi = _client.GetWriteApi();
            var docList = documents.Where(d => d != null && d.Count > 0).ToList();

            if (docList.Count == 0) return;

            foreach (var doc in docList)
            {
                var measurement = doc.TryGetValue("_measurement", out var m) ? m?.ToString() ?? "migration_data" : "migration_data";
                var point = PointData.Measurement(measurement);

                foreach (var kvp in doc)
                {
                    if (kvp.Key.StartsWith("_") || kvp.Key == "time")
                        continue;

                    if (kvp.Value is string s)
                        point = point.Tag(kvp.Key, s);
                    else
                        point = point.Field(kvp.Key, Convert.ToDouble(kvp.Value ?? 0));
                }

                writeApi.WritePoint(point, collection, _organization);
            }

            writeApi.Flush();
        }

        private static string ConvertFilterToFluxPredicate(string filterJson)
        {
            // Simple conversion — return as-is for basic predicates
            return filterJson;
        }
    }
}
