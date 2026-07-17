using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Birko.Data.Models;
using Birko.Serialization;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;

namespace Birko.Workflow.XML.Models;

[XmlRoot("WorkflowInstance")]
public class XmlWorkflowInstanceModel : AbstractModel
{
    // CR-L418: history is persisted as a list of this XmlSerializer-friendly DTO. The core
    // StateChangeRecord is a positional record (no parameterless ctor) and WorkflowInstance.History is
    // typed IReadOnlyList<T> (an interface) — System.Xml.XmlSerializer can serialize neither, so
    // FromInstance/UpdateFromInstance previously threw on EVERY save (the backend was never exercised
    // because it had no tests). Map StateChangeRecord to/from this POCO and serialize a concrete list.
    public class XmlStateChangeRecord
    {
        public string FromState { get; set; } = string.Empty;
        public string ToState { get; set; } = string.Empty;
        public string Trigger { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; }
    }
    [XmlElement("WorkflowName")]
    public string WorkflowName { get; set; } = string.Empty;

    [XmlElement("CurrentState")]
    public string CurrentState { get; set; } = string.Empty;

    [XmlElement("Status")]
    public int Status { get; set; }

    [XmlElement("DataXml")]
    public string DataXml { get; set; } = string.Empty;

    // CR-M275 / CR-L418: the XmlSerializer root for List<XmlStateChangeRecord> is
    // <ArrayOfXmlStateChangeRecord> (the .NET ArrayOf{TypeName} convention). The default must be the
    // empty-array root for the DTO element name so ToInstance can deserialize a model whose HistoryXml
    // was never overwritten by FromInstance/UpdateFromInstance (an unmatched root throws
    // "<...> was not expected"). Use a value that actually round-trips as an empty list.
    [XmlElement("HistoryXml")]
    public string HistoryXml { get; set; } = "<ArrayOfXmlStateChangeRecord />";

    [XmlElement("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [XmlElement("UpdatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    private static readonly ISerializer DefaultSerializer = new Birko.Serialization.Xml.SystemXmlSerializer();

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

        // STORY-029: DataXml defaults to string.Empty (invalid XML); guard empty/whitespace + a null
        // deserialize result with a clear error instead of forcing a null into Restore with `!`.
        if (string.IsNullOrWhiteSpace(DataXml))
        {
            throw new InvalidOperationException(
                $"Workflow instance '{Guid}' has empty DataXml and cannot be restored (workflow '{WorkflowName}').");
        }

        var data = s.Deserialize<TData>(DataXml)
                   ?? throw new InvalidOperationException(
                       $"Workflow instance '{Guid}' DataXml deserialized to null and cannot be restored (workflow '{WorkflowName}').");
        // CR-M275: guard against an empty/whitespace HistoryXml so the new-List fallback is reachable
        // regardless of the stored literal (System.Xml is quirky around empty collection roots).
        // CR-L418: deserialize into the XmlSerializer-friendly DTO list, then map back to StateChangeRecord.
        var dtoHistory = string.IsNullOrWhiteSpace(HistoryXml)
                         ? new List<XmlStateChangeRecord>()
                         : s.Deserialize<List<XmlStateChangeRecord>>(HistoryXml) ?? new List<XmlStateChangeRecord>();
        var history = dtoHistory
            .Select(r => new StateChangeRecord(r.FromState, r.ToState, r.Trigger, r.OccurredAt))
            .ToList();

        return WorkflowInstance<TData>.Restore(
            Guid.Value,
            CurrentState,
            (WorkflowStatus)Status,
            data,
            history);
    }

    // CR-L418: map the core positional-record history into XmlSerializer-friendly DTOs, materialized as a
    // concrete List (WorkflowInstance.History is an IReadOnlyList<T> interface XmlSerializer cannot handle).
    private static List<XmlStateChangeRecord> ToDtoHistory(IEnumerable<StateChangeRecord> history) =>
        history.Select(r => new XmlStateChangeRecord
        {
            FromState = r.FromState,
            ToState = r.ToState,
            Trigger = r.Trigger,
            OccurredAt = r.OccurredAt
        }).ToList();

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
            HistoryXml = s.Serialize(ToDtoHistory(instance.History)),
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
        HistoryXml = s.Serialize(ToDtoHistory(instance.History));
        UpdatedAt = DateTime.UtcNow;
    }
}
