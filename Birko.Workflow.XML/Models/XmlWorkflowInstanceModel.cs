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

    // CR-M275: the XmlSerializer root for List<StateChangeRecord> is <ArrayOfStateChangeRecord> (the
    // .NET ArrayOf{TypeName} convention); the old "<ArrayOfTypeName />" placeholder matched no type and
    // made ToInstance throw ("<ArrayOfTypeName> was not expected") on a model whose HistoryXml wasn't
    // overwritten by FromInstance/UpdateFromInstance. Use a value that actually round-trips as empty.
    [XmlElement("HistoryXml")]
    public string HistoryXml { get; set; } = "<ArrayOfStateChangeRecord />";

    [XmlElement("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [XmlElement("UpdatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    private static readonly ISerializer DefaultSerializer = new Birko.Serialization.Xml.SystemXmlSerializer();

    public WorkflowInstance<TData> ToInstance<TData>(ISerializer? serializer = null) where TData : class
    {
        var s = serializer ?? DefaultSerializer;
        var data = s.Deserialize<TData>(DataXml)!;
        // CR-M275: guard against an empty/whitespace HistoryXml so the new-List fallback is reachable
        // regardless of the stored literal (System.Xml is quirky around empty collection roots).
        var history = string.IsNullOrWhiteSpace(HistoryXml)
                      ? new List<StateChangeRecord>()
                      : s.Deserialize<List<StateChangeRecord>>(HistoryXml) ?? new List<StateChangeRecord>();

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
