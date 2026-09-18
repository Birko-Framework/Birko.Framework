using System;
using System.Collections.Generic;
using Birko.Data.Models;
using Birko.Serialization;
using Birko.Serialization.Json;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using MongoDB.Bson.Serialization.Attributes;

namespace Birko.Workflow.MongoDB.Models;

public class MongoWorkflowInstanceModel : AbstractModel
{
    [BsonElement("workflowName")]
    public string WorkflowName { get; set; } = string.Empty;

    [BsonElement("currentState")]
    public string CurrentState { get; set; } = string.Empty;

    [BsonElement("status")]
    public int Status { get; set; }

    [BsonElement("dataJson")]
    public string DataJson { get; set; } = string.Empty;

    [BsonElement("historyJson")]
    public string HistoryJson { get; set; } = "[]";

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // STORY-029: route (de)serialization through Birko.Serialization.ISerializer (injectable, camelCase
    // SystemJsonSerializer default) so all workflow backends share one seam and wire format.
    private static readonly ISerializer DefaultSerializer = new SystemJsonSerializer();

    public WorkflowInstance<TData> ToInstance<TData>(ISerializer? serializer = null) where TData : class
    {
        var s = serializer ?? DefaultSerializer;

        // STORY-029: a persisted document with no Guid is corrupt — minting a random InstanceId would
        // diverge from the document id and duplicate on the next SaveAsync upsert (matches ES CR-L406).
        if (Guid == null)
        {
            throw new InvalidOperationException(
                $"Workflow instance document has no Guid and cannot be restored (workflow '{WorkflowName}').");
        }

        // CR-L411: DataJson defaults to string.Empty (invalid JSON) and Deserialize<TData> returns a
        // nullable T; the old `!` masked a genuinely-null payload (empty / "null" / deserialize-to-null),
        // deferring a NullReferenceException to every consumer of instance.Data. Fail fast with a clear
        // error instead, mirroring the History `??` fallback's explicit handling.
        if (string.IsNullOrWhiteSpace(DataJson))
        {
            throw new InvalidOperationException(
                $"Workflow instance '{Guid}' has empty DataJson and cannot be restored (workflow '{WorkflowName}').");
        }

        var data = s.Deserialize<TData>(DataJson)
                   ?? throw new InvalidOperationException(
                       $"Workflow instance '{Guid}' DataJson deserialized to null and cannot be restored (workflow '{WorkflowName}').");
        var history = s.Deserialize<List<StateChangeRecord>>(HistoryJson)
                      ?? new List<StateChangeRecord>();

        return WorkflowInstance<TData>.Restore(
            Guid.Value,
            CurrentState,
            (WorkflowStatus)Status,
            data,
            history);
    }

    public static MongoWorkflowInstanceModel FromInstance<TData>(string workflowName, WorkflowInstance<TData> instance, ISerializer? serializer = null)
        where TData : class
    {
        var s = serializer ?? DefaultSerializer;
        return new MongoWorkflowInstanceModel
        {
            Guid = instance.InstanceId,
            WorkflowName = workflowName,
            CurrentState = instance.CurrentState,
            Status = (int)instance.Status,
            DataJson = s.Serialize(instance.Data),
            HistoryJson = s.Serialize(instance.History),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void UpdateFromInstance<TData>(WorkflowInstance<TData> instance, ISerializer? serializer = null) where TData : class
    {
        var s = serializer ?? DefaultSerializer;
        CurrentState = instance.CurrentState;
        Status = (int)instance.Status;
        DataJson = s.Serialize(instance.Data);
        HistoryJson = s.Serialize(instance.History);
        UpdatedAt = DateTime.UtcNow;
    }
}
