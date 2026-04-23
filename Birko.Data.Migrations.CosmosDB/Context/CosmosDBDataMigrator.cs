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

    public CosmosDBDataMigrator(Database database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public void UpdateDocuments(string collection, string filterJson, IDictionary<string, object> updates)
    {
        if (updates == null || updates.Count == 0) return;

        var container = _database.GetContainer(collection);
        var patchOperations = updates.Select(kvp =>
            PatchOperation.Set($"/{kvp.Key}", kvp.Value)
        ).ToArray();

        var whereClause = ParseFilterToSql(filterJson);
        var query = string.IsNullOrEmpty(whereClause)
            ? "SELECT c.id FROM c"
            : $"SELECT c.id FROM c WHERE {whereClause}";

        var iterator = container.GetItemQueryIterator<dynamic>(new QueryDefinition(query));

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            foreach (var item in response)
            {
                string? id = item.id?.ToString();
                if (id != null)
                {
                    container.PatchItemAsync<dynamic>(id, new PartitionKey(id), patchOperations)
                        .GetAwaiter().GetResult();
                }
            }
        }
    }

    public void DeleteDocuments(string collection, string filterJson)
    {
        var container = _database.GetContainer(collection);
        var whereClause = ParseFilterToSql(filterJson);
        var query = string.IsNullOrEmpty(whereClause)
            ? "SELECT c.id FROM c"
            : $"SELECT c.id FROM c WHERE {whereClause}";

        var iterator = container.GetItemQueryIterator<dynamic>(new QueryDefinition(query));

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            foreach (var item in response)
            {
                string? id = item.id?.ToString();
                if (id != null)
                {
                    container.DeleteItemAsync<dynamic>(id, new PartitionKey(id))
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

        var iterator = sourceContainer.GetItemQueryIterator<dynamic>(new QueryDefinition("SELECT * FROM c"));

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            foreach (var item in response)
            {
                string? id = item.id?.ToString();
                if (id != null)
                {
                    targetContainer.UpsertItemAsync(item, new PartitionKey(id))
                        .GetAwaiter().GetResult();
                }
            }
        }
    }

    public void BulkInsert(string collection, IEnumerable<IDictionary<string, object>> documents)
    {
        if (documents == null) return;

        var container = _database.GetContainer(collection);
        var tasks = documents
            .Where(d => d != null && d.Count > 0)
            .Select(doc =>
            {
                var id = doc.TryGetValue("id", out var idVal) ? idVal?.ToString() : Guid.NewGuid().ToString();
                return container.CreateItemAsync(doc, new PartitionKey(id));
            }).ToArray();

        if (tasks.Length > 0)
        {
            System.Threading.Tasks.Task.WhenAll(tasks).GetAwaiter().GetResult();
        }
    }

    private static string ParseFilterToSql(string? filterJson)
    {
        if (string.IsNullOrWhiteSpace(filterJson) || filterJson!.Trim() == "{}")
            return string.Empty;

        using var doc = JsonDocument.Parse(filterJson);
        var conditions = new List<string>();

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            var fieldName = $"c.{property.Name}";

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

    private static string FormatSqlValue(object? value)
    {
        if (value == null) return "null";
        if (value is string s) return $"'{s.Replace("'", "''")}'";
        if (value is bool b) return b ? "true" : "false";
        if (value is DateTime dt) return $"'{dt:yyyy-MM-ddTHH:mm:ssZ}'";
        return value.ToString() ?? "null";
    }

    private static object? ExtractValue(JsonElement element)
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
