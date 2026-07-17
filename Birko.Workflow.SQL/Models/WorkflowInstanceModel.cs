using System;
using System.Collections.Generic;
using Birko.Data.Models;
using Birko.Serialization;
using Birko.Serialization.Json;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;

namespace Birko.Workflow.SQL.Models
{
    [Birko.Data.SQL.Attributes.Table("__WorkflowInstances")]
    public class WorkflowInstanceModel : AbstractModel
    {
        [Birko.Data.SQL.Attributes.PrimaryField]
        [Birko.Data.SQL.Attributes.NamedField("Id")]
        public override Guid? Guid { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("WorkflowName")]
        public string WorkflowName { get; set; } = string.Empty;

        [Birko.Data.SQL.Attributes.NamedField("CurrentState")]
        public string CurrentState { get; set; } = string.Empty;

        [Birko.Data.SQL.Attributes.NamedField("Status")]
        public int Status { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("DataJson")]
        public string DataJson { get; set; } = string.Empty;

        [Birko.Data.SQL.Attributes.NamedField("HistoryJson")]
        public string HistoryJson { get; set; } = "[]";

        [Birko.Data.SQL.Attributes.NamedField("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Birko.Data.SQL.Attributes.NamedField("UpdatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // CR-L416 / STORY-029: route through Birko.Serialization.ISerializer for an injectable seam.
        // STORY-029 aligned the default to camelCase (the framework's deliberate convention wherever the
        // abstraction is used for persistence — BackgroundJobs, the Data.JSON store, and the Workflow.JSON
        // backend are all camelCase). Verified no persisted workflow data exists, so the earlier
        // PascalCase-preservation (CR-L416) is unnecessary; the whole workflow-backend family is now camelCase.
        private static readonly ISerializer DefaultSerializer = new SystemJsonSerializer();

        public WorkflowInstance<TData> ToInstance<TData>(ISerializer? serializer = null) where TData : class
        {
            // CR-L416: route (de)serialization through Birko.Serialization.ISerializer (injectable,
            // SystemJsonSerializer default) so this backend shares the JSON reference model's seam
            // rather than calling System.Text.Json directly.
            var s = serializer ?? DefaultSerializer;

            // STORY-029: a persisted row with no Guid is corrupt — minting a random InstanceId would
            // diverge from the stored id and duplicate on the next SaveAsync upsert (matches ES CR-L406).
            if (Guid == null)
            {
                throw new InvalidOperationException(
                    $"Workflow instance row has no Guid and cannot be restored (workflow '{WorkflowName}').");
            }

            // CR-L415: DataJson defaults to string.Empty (invalid JSON) and Deserialize<TData> returns a
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

        public static WorkflowInstanceModel FromInstance<TData>(string workflowName, WorkflowInstance<TData> instance, ISerializer? serializer = null)
            where TData : class
        {
            var s = serializer ?? DefaultSerializer;
            return new WorkflowInstanceModel
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
}
