using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Birko.Data.Migrations.Context;
using Microsoft.Azure.Cosmos;

namespace Birko.Data.Migrations.CosmosDB.Context;

public class CosmosDBDataMigrator : IDataMigrator
{
    private readonly Database _database;

    // Caches each container's top-level partition-key property name so point operations use the
    // real partition key rather than assuming it is /id (CR-H055).
    private readonly Dictionary<string, string> _partitionKeyProperties = new();

    public CosmosDBDataMigrator(Database database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// Returns the top-level partition-key property name for a container (e.g. "/tenantId" -&gt;
    /// "tenantId"), read from the container definition and cached. Only yields "id" when the
    /// container's partition key path actually is /id.
    /// </summary>
    private string GetPartitionKeyProperty(Container container, string collection)
    {
        if (_partitionKeyProperties.TryGetValue(collection, out var cached))
        {
            return cached;
        }

        var path = container.ReadContainerAsync().GetAwaiter().GetResult().Resource.PartitionKeyPath ?? "/id";
        var property = path.TrimStart('/').Split('/')[0];
        if (string.IsNullOrEmpty(property)) property = "id";
        _partitionKeyProperties[collection] = property;
        return property;
    }

    /// <summary>
    /// Builds a typed PartitionKey from a document's partition-key field (CR-H055). Uses
    /// System.Text.Json to match the CosmosDB store's serializer convention (CosmosGuidIdSerializer),
    /// not Newtonsoft.
    /// </summary>
    internal static PartitionKey BuildPartitionKey(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var el))
        {
            return PartitionKey.Null;
        }
        return el.ValueKind switch
        {
            JsonValueKind.String => new PartitionKey(el.GetString()),
            JsonValueKind.Number => new PartitionKey(el.GetDouble()),
            JsonValueKind.True => new PartitionKey(true),
            JsonValueKind.False => new PartitionKey(false),
            _ => PartitionKey.Null
        };
    }

    private PartitionKey ResolvePartitionKey(Container container, string collection, string id, JsonElement item)
    {
        var pkProperty = GetPartitionKeyProperty(container, collection);
        return pkProperty == "id" ? new PartitionKey(id) : BuildPartitionKey(item, pkProperty);
    }

    public void UpdateDocuments(string collection, string filterJson, IDictionary<string, object> updates)
    {
        if (updates == null || updates.Count == 0) return;

        // SH-H032: an empty clause means either "no filter" or "every term was dropped" (e.g.
        // {"status":{}} takes the object branch and the operator loop adds nothing). Only the first is a
        // deliberate match-all; the second used to select -- and then patch -- every document.
        //
        // Parsed and guarded BEFORE GetPartitionKeyProperty, which issues a ReadContainerAsync round trip:
        // refusing should not first cost a network call, and it keeps the refusal reachable without a live
        // account so it can be asserted offline (§ TASK-309).
        var parameters = new List<KeyValuePair<string, object?>>();
        var whereClause = ParseFilterToSql(filterJson, parameters);
        Birko.Data.Migrations.Context.MigrationFilter.RequireBounded(filterJson, whereClause.Length > 0,
            "update", collection, "every document in the container");

        var container = _database.GetContainer(collection);
        var patchOperations = updates.Select(kvp =>
            PatchOperation.Set($"/{kvp.Key}", kvp.Value)
        ).ToArray();

        var pkProperty = GetPartitionKeyProperty(container, collection);
        var projection = pkProperty == "id" ? "c.id" : $"c.id, c.{pkProperty}";
        var query = string.IsNullOrEmpty(whereClause)
            ? $"SELECT {projection} FROM c"
            : $"SELECT {projection} FROM c WHERE {whereClause}";

        var iterator = container.GetItemQueryIterator<JsonElement>(Bind(query, parameters));

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            foreach (var item in response)
            {
                string? id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (id != null)
                {
                    container.PatchItemAsync<dynamic>(id, ResolvePartitionKey(container, collection, id, item), patchOperations)
                        .GetAwaiter().GetResult();
                }
            }
        }
    }

    public void DeleteDocuments(string collection, string filterJson)
    {
        // SH-H032 -- see UpdateDocuments. This path used to select every document and delete each one.
        var parameters = new List<KeyValuePair<string, object?>>();
        var whereClause = ParseFilterToSql(filterJson, parameters);
        Birko.Data.Migrations.Context.MigrationFilter.RequireBounded(filterJson, whereClause.Length > 0,
            "delete", collection, "every document in the container");

        var container = _database.GetContainer(collection);
        var pkProperty = GetPartitionKeyProperty(container, collection);
        var projection = pkProperty == "id" ? "c.id" : $"c.id, c.{pkProperty}";
        var query = string.IsNullOrEmpty(whereClause)
            ? $"SELECT {projection} FROM c"
            : $"SELECT {projection} FROM c WHERE {whereClause}";

        var iterator = container.GetItemQueryIterator<JsonElement>(Bind(query, parameters));

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            foreach (var item in response)
            {
                string? id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (id != null)
                {
                    container.DeleteItemAsync<dynamic>(id, ResolvePartitionKey(container, collection, id, item))
                        .GetAwaiter().GetResult();
                }
            }
        }
    }

    public long CountDocuments(string collection, string? filterJson = null)
    {
        var parameters = new List<KeyValuePair<string, object?>>();
        var whereClause = ParseFilterToSql(filterJson, parameters);
        var container = _database.GetContainer(collection);
        // SH-H032: guard the read too, so a count cannot silently answer for the whole container while a
        // delete built from the same filter is refused (§ TASK-215, § TASK-313).
        Birko.Data.Migrations.Context.MigrationFilter.RequireBounded(filterJson, whereClause.Length > 0,
            "count", collection, "every document in the container");
        var query = string.IsNullOrEmpty(whereClause)
            ? "SELECT VALUE COUNT(1) FROM c"
            : $"SELECT VALUE COUNT(1) FROM c WHERE {whereClause}";

        var iterator = container.GetItemQueryIterator<long>(Bind(query, parameters));
        var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
        return response.FirstOrDefault();
    }

    public void CopyData(string sourceCollection, string targetCollection, string? transformJson = null)
    {
        var sourceContainer = _database.GetContainer(sourceCollection);
        var targetContainer = _database.GetContainer(targetCollection);

        var iterator = sourceContainer.GetItemQueryIterator<JsonElement>(new QueryDefinition("SELECT * FROM c"));

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            foreach (var item in response)
            {
                string? id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (id != null)
                {
                    targetContainer.UpsertItemAsync(item, ResolvePartitionKey(targetContainer, targetCollection, id, item))
                        .GetAwaiter().GetResult();
                }
            }
        }
    }

    public void BulkInsert(string collection, IEnumerable<IDictionary<string, object>> documents)
    {
        if (documents == null) return;

        var container = _database.GetContainer(collection);
        var pkProperty = GetPartitionKeyProperty(container, collection);
        var tasks = documents
            .Where(d => d != null && d.Count > 0)
            .Select(doc =>
            {
                var id = doc.TryGetValue("id", out var idVal) ? idVal?.ToString() : Guid.NewGuid().ToString();
                // Use the container's real partition key, not the id (CR-H055). Falls back to id when
                // the PK path is /id or the document omits the partition-key field.
                PartitionKey pk;
                if (pkProperty != "id" && doc.TryGetValue(pkProperty, out var pkVal) && pkVal != null)
                {
                    pk = pkVal switch
                    {
                        bool b => new PartitionKey(b),
                        string s => new PartitionKey(s),
                        _ when pkVal is IConvertible => new PartitionKey(Convert.ToDouble(pkVal)),
                        _ => new PartitionKey(pkVal.ToString())
                    };
                }
                else
                {
                    pk = new PartitionKey(id);
                }
                return container.CreateItemAsync(doc, pk);
            }).ToArray();

        if (tasks.Length > 0)
        {
            System.Threading.Tasks.Task.WhenAll(tasks).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Translates a Mongo-style filter document into a Cosmos SQL <c>WHERE</c> fragment, collecting
    /// every compared value into <paramref name="parameters"/> as an <c>@pN</c> binding.
    /// </summary>
    /// <remarks>
    /// TASK-450. Values used to be rendered into the statement by <c>FormatSqlValue</c>, which escaped
    /// a quote by **doubling** it. That is the SQL-standard rule and Cosmos NoSQL does not use it --
    /// it escapes with a backslash -- so the doubling produced two adjacent literals (a syntax error)
    /// while a **backslash in the value was never escaped at all** and closed the literal early.
    /// Measured: <c>a\' OR 1=1 --</c> rendered as <c>'a\'' OR 1=1 --'</c>, which lexes as the string
    /// <c>a'</c> followed by <c>OR 1=1</c> and a comment.
    ///
    /// Parameterised rather than escaped correctly, matching what TASK-447 did for
    /// <c>CosmosViewStore</c>: all three call sites already built a <c>QueryDefinition</c> and simply
    /// never bound anything, and a bound value has no grammar to break out of.
    /// </remarks>
    internal static string ParseFilterToSql(string? filterJson, IList<KeyValuePair<string, object?>> parameters)
    {
        if (string.IsNullOrWhiteSpace(filterJson) || filterJson!.Trim() == "{}")
            return string.Empty;

        using var doc = JsonDocument.Parse(filterJson);
        var conditions = new List<string>();

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            // CR-M104's rule, unchanged -- only moved, so CosmosDBSchemaBuilder can share it
            // (TASK-450: it interpolated a field name with no escaping at all).
            var fieldName = QuoteFieldPath(property.Name);

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var op in property.Value.EnumerateObject())
                {
                    var sqlOp = op.Name switch
                    {
                        "$gt" => ">",
                        "$gte" => ">=",
                        "$lt" => "<",
                        "$lte" => "<=",
                        "$ne" => "!=",
                        _ => "="
                    };
                    conditions.Add($"{fieldName} {sqlOp} {BindValue(ExtractValue(op.Value), parameters)}");
                }
            }
            else
            {
                conditions.Add($"{fieldName} = {BindValue(ExtractValue(property.Value), parameters)}");
            }
        }

        return string.Join(" AND ", conditions);
    }

    /// <summary>
    /// Binds a compared value as an <c>@pN</c> query parameter and returns the placeholder.
    /// </summary>
    /// <remarks>
    /// Replaces the former <c>FormatSqlValue</c>, which rendered the value as a SQL literal. See
    /// <see cref="ParseFilterToSql"/> for why that could not be made safe by escaping harder.
    ///
    /// A side effect worth naming: the old <c>DateTime</c> branch formatted as
    /// <c>yyyy-MM-ddTHH:mm:ssZ</c>, which silently dropped sub-second precision and ignored
    /// <c>Kind</c>. Binding the value hands that to the SDK's serializer -- the same one that wrote
    /// the documents -- so the comparison matches the stored form by construction.
    /// </remarks>
    internal static string BindValue(object? value, IList<KeyValuePair<string, object?>> parameters)
    {
        var name = "@p" + parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        parameters.Add(new KeyValuePair<string, object?>(name, value));
        return name;
    }

    /// <summary>
    /// Renders a document field name as a bracket-quoted Cosmos path, e.g. <c>c["my field"]</c>.
    /// </summary>
    /// <remarks>
    /// CR-M104's escaping, unchanged in behaviour and moved here so it has one producer. A field name
    /// is an **identifier**, so unlike a compared value it cannot be parameterised and escaping is the
    /// only containment available -- which is why the two halves of this file are treated differently.
    /// Backslash is escaped first, then the double quote, or the escape introduced for the quote would
    /// itself be escaped.
    /// </remarks>
    internal static string QuoteFieldPath(string fieldName)
    {
        var escaped = fieldName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"c[\"{escaped}\"]";
    }

    /// <summary>Builds the query and binds everything <see cref="ParseFilterToSql"/> collected.</summary>
    internal static QueryDefinition Bind(string sql, List<KeyValuePair<string, object?>> parameters)
    {
        var queryDef = new QueryDefinition(sql);
        foreach (var p in parameters)
        {
            queryDef = queryDef.WithParameter(p.Key, p.Value);
        }

        return queryDef;
    }

    internal static object? ExtractValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }
}
