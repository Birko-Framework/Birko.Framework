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

        var container = _database.GetContainer(collection);
        var patchOperations = updates.Select(kvp =>
            PatchOperation.Set($"/{kvp.Key}", kvp.Value)
        ).ToArray();

        var pkProperty = GetPartitionKeyProperty(container, collection);
        var projection = pkProperty == "id" ? "c.id" : $"c.id, c.{pkProperty}";
        var whereClause = ParseFilterToSql(filterJson);
        var query = string.IsNullOrEmpty(whereClause)
            ? $"SELECT {projection} FROM c"
            : $"SELECT {projection} FROM c WHERE {whereClause}";

        var iterator = container.GetItemQueryIterator<JsonElement>(new QueryDefinition(query));

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
        var container = _database.GetContainer(collection);
        var pkProperty = GetPartitionKeyProperty(container, collection);
        var projection = pkProperty == "id" ? "c.id" : $"c.id, c.{pkProperty}";
        var whereClause = ParseFilterToSql(filterJson);
        var query = string.IsNullOrEmpty(whereClause)
            ? $"SELECT {projection} FROM c"
            : $"SELECT {projection} FROM c WHERE {whereClause}";

        var iterator = container.GetItemQueryIterator<JsonElement>(new QueryDefinition(query));

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
        var container = _database.GetContainer(collection);
        var whereClause = ParseFilterToSql(filterJson);
        var query = string.IsNullOrEmpty(whereClause)
            ? "SELECT VALUE COUNT(1) FROM c"
            : $"SELECT VALUE COUNT(1) FROM c WHERE {whereClause}";

        var iterator = container.GetItemQueryIterator<long>(new QueryDefinition(query));
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

    internal static string ParseFilterToSql(string? filterJson)
    {
        if (string.IsNullOrWhiteSpace(filterJson) || filterJson!.Trim() == "{}")
            return string.Empty;

        using var doc = JsonDocument.Parse(filterJson);
        var conditions = new List<string>();

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            // CR-M104: bracket-quote the identifier (values were already escaped, identifiers were not),
            // so a field name with whitespace/special chars can't produce malformed/injectable SQL.
            var escaped = property.Name.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var fieldName = $"c[\"{escaped}\"]";

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
                    var valueLiteral = FormatSqlValue(ExtractValue(op.Value));
                    conditions.Add($"{fieldName} {sqlOp} {valueLiteral}");
                }
            }
            else
            {
                var valueLiteral = FormatSqlValue(ExtractValue(property.Value));
                conditions.Add($"{fieldName} = {valueLiteral}");
            }
        }

        return string.Join(" AND ", conditions);
    }

    internal static string FormatSqlValue(object? value)
    {
        if (value == null) return "null";
        if (value is string s) return $"'{s.Replace("'", "''")}'";
        if (value is bool b) return b ? "true" : "false";
        if (value is DateTime dt) return $"'{dt:yyyy-MM-ddTHH:mm:ssZ}'";
        return value.ToString() ?? "null";
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
