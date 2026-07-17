using System;
using System.Collections.Generic;
using Birko.Data.Models;
using Birko.Serialization;
using Birko.Serialization.Json;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Nest;

namespace Birko.Workflow.ElasticSearch.Models;

public class ElasticWorkflowInstanceModel : AbstractModel
{
    [Keyword(Name = "workflowName")]
    public string WorkflowName { get; set; } = string.Empty;

    [Keyword(Name = "currentState")]
    public string CurrentState { get; set; } = string.Empty;

    [Number(NumberType.Integer, Name = "status")]
    public int Status { get; set; }

    [Text(Name = "dataJson", Index = false)]
    public string DataJson { get; set; } = string.Empty;

    [Text(Name = "historyJson", Index = false)]
    public string HistoryJson { get; set; } = "[]";

    [Date(Name = "createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Date(Name = "updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public const string IndexName = "workflow-instances";

    // STORY-029: route (de)serialization through Birko.Serialization.ISerializer (injectable, camelCase
    // SystemJsonSerializer default) so all workflow backends share one seam and wire format.
    private static readonly ISerializer DefaultSerializer = new SystemJsonSerializer();

    public WorkflowInstance<TData> ToInstance<TData>(ISerializer? serializer = null) where TData : class
    {
        var s = serializer ?? DefaultSerializer;

        // CR-L406: a persisted document with no Guid is corrupt. Minting a random InstanceId here
        // would diverge from the document id, so the next SaveAsync upsert (matched on Guid) would
        // miss the row and create a duplicate — surface the bad record instead.
        if (Guid == null)
        {
            throw new InvalidOperationException(
                $"Workflow instance document has no Guid and cannot be restored (workflow '{WorkflowName}').");
        }

        // CR-L405: DataJson defaults to string.Empty, which is invalid JSON — deserializing it would
        // throw an opaque JsonException. Treat an unpopulated/cleared payload (or one that deserializes
        // to null) as a corrupt record with a clear message rather than suppressing the null with `!`.
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

    public static ElasticWorkflowInstanceModel FromInstance<TData>(string workflowName, WorkflowInstance<TData> instance, ISerializer? serializer = null)
        where TData : class
    {
        var s = serializer ?? DefaultSerializer;
        return new ElasticWorkflowInstanceModel
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
