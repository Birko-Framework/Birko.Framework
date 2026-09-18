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
            // Synchronous WriteApiAsync (no leaked background batching worker) — see CR-H060.
            var writeApi = _client.GetWriteApiAsync();

            var flux = $"from(bucket: \"{sourceCollection}\")"
                + " |> range(start: -100y)"
                + " |> filter(fn: (r) => true)";

            var tables = queryApi.QueryAsync(flux, _organization).GetAwaiter().GetResult();

            var points = new List<PointData>();
            foreach (var table in tables)
            {
                foreach (var record in table.Records)
                {
                    var point = PointData.Measurement(record.GetMeasurement())
                        .Tag("_original_bucket", sourceCollection);

                    // Copy all fields from the record, preserving each value's runtime type (CR-M110:
                    // the old blanket Convert.ToDouble collapsed bool/int/long to double and threw on
                    // DateTime/byte[]).
                    foreach (var entry in record.Values)
                    {
                        var key = entry.Key;
                        if (key == "_measurement" || key == "_time" || key == "_start" || key == "_stop")
                            continue;

                        point = ApplyValue(point, key, entry.Value);
                    }

                    // Preserve the original timestamp instead of re-stamping to write time (CR-M110).
                    if (record.GetTimeInDateTime() is DateTime originalTime)
                    {
                        point = point.Timestamp(originalTime, WritePrecision.Ns);
                    }

                    points.Add(point);
                }
            }

            if (points.Count > 0)
            {
                writeApi.WritePointsAsync(points, targetCollection, _organization).GetAwaiter().GetResult();
            }
        }

        public void BulkInsert(string collection, IEnumerable<IDictionary<string, object>> documents)
        {
            if (documents == null) return;

            // Synchronous WriteApiAsync (no leaked background batching worker) — see CR-H060.
            var writeApi = _client.GetWriteApiAsync();
            var docList = documents.Where(d => d != null && d.Count > 0).ToList();

            if (docList.Count == 0) return;

            var points = new List<PointData>();
            foreach (var doc in docList)
            {
                var measurement = doc.TryGetValue("_measurement", out var m) ? m?.ToString() ?? "migration_data" : "migration_data";
                var point = PointData.Measurement(measurement);

                foreach (var kvp in doc)
                {
                    if (kvp.Key.StartsWith("_") || kvp.Key == "time")
                        continue;

                    // CR-M110: preserve the value's runtime type instead of coercing everything to double.
                    point = ApplyValue(point, kvp.Key, kvp.Value);
                }

                // Preserve an explicit timestamp if the document carries one (CR-M110).
                if ((doc.TryGetValue("_time", out var tv) || doc.TryGetValue("time", out tv)) && tv is DateTime docTime)
                {
                    point = point.Timestamp(docTime, WritePrecision.Ns);
                }

                points.Add(point);
            }

            writeApi.WritePointsAsync(points, collection, _organization).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Writes a value onto a point, preserving its runtime type: strings become tags; bool / integer
        /// / floating-point map to their matching Field overload; null is skipped; anything else (e.g.
        /// DateTime, byte[]) is written as its string representation rather than throwing (CR-M110).
        /// </summary>
        internal static PointData ApplyValue(PointData point, string key, object? value)
        {
            switch (value)
            {
                case null:
                    return point;
                case string s:
                    return point.Tag(key, s);
                case bool b:
                    return point.Field(key, b);
                case sbyte or byte or short or ushort or int or uint or long:
                    return point.Field(key, Convert.ToInt64(value));
                case ulong ul:
                    return point.Field(key, (long)ul);
                case float or double or decimal:
                    return point.Field(key, Convert.ToDouble(value));
                default:
                    return point.Field(key, value.ToString() ?? string.Empty);
            }
        }

        internal static string ConvertFilterToFluxPredicate(string filterJson)
        {
            if (string.IsNullOrWhiteSpace(filterJson))
            {
                return string.Empty;
            }

            var trimmed = filterJson.Trim();
            if (trimmed == "{}")
            {
                return string.Empty;
            }

            // CR-M111: a JSON object filter cannot be used as an InfluxDB delete predicate (which is a
            // boolean Flux expression like `_measurement="m" AND tag="v"`). The old code returned it
            // verbatim, so a JSON filter silently matched nothing / everything. Reject it clearly; a
            // caller that already has a Flux predicate string can still pass it through.
            if (trimmed.StartsWith("{"))
            {
                throw new NotSupportedException(
                    "InfluxDB delete does not support JSON filters. Pass a Flux delete predicate string " +
                    "(e.g. '_measurement=\"m\" AND tag=\"v\"'), or an empty filter to delete the whole range.");
            }

            return filterJson;
        }
    }
}
