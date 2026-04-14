using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Birko.Data.Models;
using Birko.Serialization;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;

namespace Birko.Workflow.XML.Models;

[XmlRoot("WorkflowInstance")]
public class XmlWorkflowInstanceModel : AbstractModel
{
    [XmlElement("WorkflowName")]
    public string WorkflowName { get; set; } = string.Empty;

    [XmlElement("CurrentState")]
    public string CurrentState { get; set; } = string.Empty;

    [XmlElement("Status")]
    public int Status { get; set; }

    [XmlElement("DataXml")]
    public string DataXml { get; set; } = string.Empty;

    [XmlElement("HistoryXml")]
    public string HistoryXml { get; set; } = "<ArrayOfTypeName />";

    [XmlElement("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [XmlElement("UpdatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    private static readonly ISerializer DefaultSerializer = new Birko.Serialization.Xml.SystemXmlSerializer();

    public WorkflowInstance<TData> ToInstance<TData>(ISerializer? serializer = null) where TData : class
    {
        var s = serializer ?? DefaultSerializer;
        var data = s.Deserialize<TData>(DataXml)!;
        var history = s.Deserialize<List<StateChangeRecord>>(HistoryXml)
                      ?? new List<StateChangeRecord>();

        return WorkflowInstance<TData>.Restore(
            Guid ?? System.Guid.NewGuid(),
            CurrentState,
            (WorkflowStatus)Status,
            data,
            history);
    }

    public static XmlWorkflowInstanceModel FromInstance<TData>(string workflowName, WorkflowInstance<TData> instance, ISerializer? serializer = null)
        where TData : class
    {
        var s = serializer ?? DefaultSerializer;
        return new XmlWorkflowInstanceModel
        {
            Guid = instance.InstanceId,
            WorkflowName = workflowName,
            CurrentState = instance.CurrentState,
            Status = (int)instance.Status,
            DataXml = s.Serialize(instance.Data),
            HistoryXml = s.Serialize(instance.History),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void UpdateFromInstance<TData>(WorkflowInstance<TData> instance, ISerializer? serializer = null) where TData : class
    {
        var s = serializer ?? DefaultSerializer;
        CurrentState = instance.CurrentState;
        Status = (int)instance.Status;
        DataXml = s.Serialize(instance.Data);
        HistoryXml = s.Serialize(instance.History);
        UpdatedAt = DateTime.UtcNow;
    }
}
