using System;
using System.Collections.Generic;
using System.Text.Json;
using Birko.Data.Models;
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

    public static ElasticWorkflowInstanceModel FromInstance<TData>(string workflowName, WorkflowInstance<TData> instance)
        where TData : class
    {
        return new ElasticWorkflowInstanceModel
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
