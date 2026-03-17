using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Birko.Data.Models;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;

namespace Birko.Workflow.JSON.Models;

public class JsonWorkflowInstanceModel : AbstractModel
{
    [JsonPropertyName("workflowName")]
    public string WorkflowName { get; set; } = string.Empty;

    [JsonPropertyName("currentState")]
    public string CurrentState { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("dataJson")]
    public string DataJson { get; set; } = string.Empty;

    [JsonPropertyName("historyJson")]
    public string HistoryJson { get; set; } = "[]";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public WorkflowInstance<TData> ToInstance<TData>() where TData : class
    {
        var data = JsonSerializer.Deserialize<TData>(DataJson)!;
        var history = JsonSerializer.Deserialize<List<StateChangeRecord>>(HistoryJson)
                      ?? new List<StateChangeRecord>();

        return WorkflowInstance<TData>.Restore(
            Guid ?? System.Guid.NewGuid(),
            CurrentState,
            (WorkflowStatus)Status,
            data,
            history);
    }

    public static JsonWorkflowInstanceModel FromInstance<TData>(string workflowName, WorkflowInstance<TData> instance)
        where TData : class
    {
        return new JsonWorkflowInstanceModel
        {
            Guid = instance.InstanceId,
            WorkflowName = workflowName,
            CurrentState = instance.CurrentState,
            Status = (int)instance.Status,
            DataJson = JsonSerializer.Serialize(instance.Data),
            HistoryJson = JsonSerializer.Serialize(instance.History),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void UpdateFromInstance<TData>(WorkflowInstance<TData> instance) where TData : class
    {
        CurrentState = instance.CurrentState;
        Status = (int)instance.Status;
        DataJson = JsonSerializer.Serialize(instance.Data);
        HistoryJson = JsonSerializer.Serialize(instance.History);
        UpdatedAt = DateTime.UtcNow;
    }
}
