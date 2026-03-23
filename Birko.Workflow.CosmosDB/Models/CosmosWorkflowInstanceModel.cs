using System;
using System.Collections.Generic;
using System.Text.Json;
using Birko.Data.Models;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;

namespace Birko.Workflow.CosmosDB.Models;

/// <summary>
/// Cosmos DB-persisted model for a workflow instance.
/// </summary>
public class CosmosWorkflowInstanceModel : AbstractModel
{
    public string WorkflowName { get; set; } = string.Empty;
    public string CurrentState { get; set; } = string.Empty;
    public int Status { get; set; }
    public string DataJson { get; set; } = string.Empty;
    public string HistoryJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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

    public static CosmosWorkflowInstanceModel FromInstance<TData>(string workflowName, WorkflowInstance<TData> instance)
        where TData : class
    {
        return new CosmosWorkflowInstanceModel
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
