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

        /// <summary>Refused: InfluxDB points are immutable (SH-M191).</summary>
        /// <remarks>
        /// SH-M191 (TASK-152): the body was a comment, so an update performed nothing, threw nothing, and the runner
        /// recorded the migration as applied. Every other backend updates or throws.
        /// </remarks>
        public void UpdateDocuments(string collection, string filterJson, IDictionary<string, object> updates)
            => throw new NotSupportedException(
                "InfluxDB points are immutable, so UpdateDocuments cannot change them. Delete and rewrite the series, or use Raw().");

        public void DeleteDocuments(string collection, string filterJson)
        {
            var deleteApi = _client.GetDeleteApi();
            // SH-M193 (TASK-152): a bucket that is not found is an error. The whole delete was wrapped in
            // `if (bucket != null)`, so a misspelt name deleted nothing and the migration was recorded as applied.
            var bucket = InfluxBucketLookup.Find(_client, _organization, collection)
                ?? throw new InvalidOperationException(
                    $"InfluxDB bucket '{collection}' was not found in organization '{_organization}', so nothing can be deleted from it.");

            var start = DateTime.UtcNow.AddYears(-100);
            var stop = DateTime.UtcNow;

            // Use filterJson as the Flux predicate if provided
            var predicate = string.IsNullOrWhiteSpace(filterJson) || filterJson.Trim() == "{}"
                ? ""
                : ConvertFilterToFluxPredicate(filterJson);

            // TASK-619: waited on. `Delete` returns a Task, and discarding it reported success before the
            // server answered: a refused predicate went unobserved, and the next step raced the delete.
            deleteApi.Delete(start, stop, predicate, bucket.Name, bucket.OrgID).GetAwaiter().GetResult();
        }

        public long CountDocuments(string collection, string? filterJson = null)
        {
            // SH-M192 (TASK-152): the filter was never read, so a filtered count answered the whole bucket. A delete
            // predicate is not a Flux filter function, so there is nothing to translate a filter into here: refused.
            if (!MigrationFilter.IsExplicitMatchAll(filterJson))
                throw new NotSupportedException(
                    "InfluxDB CountDocuments cannot apply a filter; count the whole bucket (no filter), or use Raw() with a Flux query.");

            var queryApi = _client.GetQueryApi();

            var flux = $"from(bucket: \"{(InfluxMigrationStore.EscapeFluxString(collection))}\")"
                + " |> range(start: 0)"
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
            MigrationFilter.RefuseTransform(transformJson, "InfluxDB"); // SH-M196 (TASK-152)

            var queryApi = _client.GetQueryApi();
            // Synchronous WriteApiAsync (no leaked background batching worker) — see CR-H060.
            var writeApi = _client.GetWriteApiAsync();

            var flux = $"from(bucket: \"{(InfluxMigrationStore.EscapeFluxString(sourceCollection))}\")"
                + " |> range(start: 0)";

            var tables = queryApi.QueryAsync(flux, _organization).GetAwaiter().GetResult();

            var points = new List<PointData>();
            foreach (var table in tables)
            {
                foreach (var record in table.Records)
                {
                    // SH-M194 (TASK-152): a Flux record is ONE field of one point: _field names it, _value holds it, and the
                    // other non-underscore columns are its tags (result and table are the query's, not the data's). Every
                    // column used to be copied, so the field name became a tag called "_field" and the value a field
                    // called "_value"; and a reserved "_original_bucket" tag was added to every point.
                    var point = PointData.Measurement(record.GetMeasurement());
                    foreach (var entry in record.Values)
                    {
                        if (entry.Key.StartsWith("_", StringComparison.Ordinal) || entry.Key is "result" or "table") continue;
                        if (entry.Value != null) point = point.Tag(entry.Key, entry.Value.ToString());
                    }
                    // CR-M110: the value keeps its runtime type.
                    point = FieldValue(point, record.GetField(), record.GetValue());

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

                // SH-M195 (TASK-152): strings are tags, and a point with no field has no line protocol, so a document of
                // strings only was dropped by the client with no error and the migration recorded.
                if (!point.HasFields())
                {
                    throw new NotSupportedException(
                        $"A document for '{collection}' has no numeric or boolean value, so it would be an InfluxDB point with no "
                        + "field, which is not written. Give it at least one field value.");
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
        /// <summary>A field value, keeping its runtime type; a string stays a string FIELD here (it was a field at the source).</summary>
        internal static PointData FieldValue(PointData point, string field, object? value) => value switch
        {
            null => point,
            string s => point.Field(field, s),
            bool b => point.Field(field, b),
            sbyte or byte or short or ushort or int or uint or long => point.Field(field, Convert.ToInt64(value)),
            ulong ul => point.Field(field, (long)ul),
            float or double or decimal => point.Field(field, Convert.ToDouble(value)),
            _ => point.Field(field, value.ToString() ?? string.Empty),
        };

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
