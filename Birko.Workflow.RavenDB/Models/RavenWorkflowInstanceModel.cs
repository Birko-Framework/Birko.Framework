using System;
using System.Collections.Generic;
using System.Text.Json;
using Birko.Data.Models;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;

namespace Birko.Workflow.RavenDB.Models;

public class RavenWorkflowInstanceModel : AbstractModel
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
        // CR-L413: DataJson defaults to string.Empty (invalid JSON) and Deserialize<TData> returns a
        // nullable T; the old `!` masked a genuinely-null payload (empty / "null" / deserialize-to-null),
        // deferring a NullReferenceException to every consumer of instance.Data. Fail fast with a clear
        // error instead, mirroring the History `??` fallback's explicit handling.
        if (string.IsNullOrWhiteSpace(DataJson))
        {
            throw new InvalidOperationException(
                $"Workflow instance '{Guid}' has empty DataJson and cannot be restored (workflow '{WorkflowName}').");
        }

        var data = JsonSerializer.Deserialize<TData>(DataJson)
                   ?? throw new InvalidOperationException(
                       $"Workflow instance '{Guid}' DataJson deserialized to null and cannot be restored (workflow '{WorkflowName}').");
        var history = JsonSerializer.Deserialize<List<StateChangeRecord>>(HistoryJson)
                      ?? new List<StateChangeRecord>();

        return WorkflowInstance<TData>.Restore(
            Guid ?? System.Guid.NewGuid(),
            CurrentState,
            (WorkflowStatus)Status,
            data,
            history);
    }

    public static RavenWorkflowInstanceModel FromInstance<TData>(string workflowName, WorkflowInstance<TData> instance)
        where TData : class
    {
        return new RavenWorkflowInstanceModel
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
