using System;
using Birko.Data.Models;
using Nest;

namespace Birko.BackgroundJobs.ElasticSearch.Models;

/// <summary>
/// Elasticsearch-persisted model for a background job descriptor.
/// Uses NEST attributes for index mapping.
/// </summary>
public class ElasticJobDescriptorModel : AbstractModel, ILoadable<JobDescriptor>
{
    [Keyword(Name = "jobType")]
    public string JobType { get; set; } = string.Empty;

    [Keyword(Name = "inputType")]
    public string? InputType { get; set; }

    [Text(Name = "serializedInput", Index = false)]
    public string? SerializedInput { get; set; }

    [Keyword(Name = "queueName")]
    public string? QueueName { get; set; }

    [Number(NumberType.Integer, Name = "priority")]
    public int Priority { get; set; }

    [Number(NumberType.Integer, Name = "maxRetries")]
    public int MaxRetries { get; set; } = 3;

    [Number(NumberType.Integer, Name = "status")]
    public int Status { get; set; }

    [Number(NumberType.Integer, Name = "attemptCount")]
    public int AttemptCount { get; set; }

    [Date(Name = "enqueuedAt")]
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;

    [Date(Name = "scheduledAt")]
    public DateTime? ScheduledAt { get; set; }

    [Date(Name = "lastAttemptAt")]
    public DateTime? LastAttemptAt { get; set; }

    [Date(Name = "completedAt")]
    public DateTime? CompletedAt { get; set; }

    [Text(Name = "lastError", Index = false)]
    public string? LastError { get; set; }

    [Text(Name = "metadataJson", Index = false)]
    public string? MetadataJson { get; set; }

    public const string IndexName = "background-jobs";

    public JobDescriptor ToDescriptor()
    {
        var descriptor = new JobDescriptor
        {
            Id = Guid ?? System.Guid.NewGuid(),
            JobType = JobType,
            InputType = InputType,
            SerializedInput = SerializedInput,
            QueueName = QueueName,
            Priority = Priority,
            MaxRetries = MaxRetries,
            Status = (JobStatus)Status,
            AttemptCount = AttemptCount,
            EnqueuedAt = EnqueuedAt,
            ScheduledAt = ScheduledAt,
            LastAttemptAt = LastAttemptAt,
            CompletedAt = CompletedAt,
            LastError = LastError
        };

        if (!string.IsNullOrEmpty(MetadataJson))
        {
            var metadata = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(MetadataJson);
            if (metadata != null)
            {
                descriptor.Metadata = metadata;
            }
        }

        return descriptor;
    }

    public static ElasticJobDescriptorModel FromDescriptor(JobDescriptor descriptor)
    {
        var model = new ElasticJobDescriptorModel();
        model.LoadFrom(descriptor);
        return model;
    }

    public void LoadFrom(JobDescriptor data)
    {
        Guid = data.Id;
        JobType = data.JobType;
        InputType = data.InputType;
        SerializedInput = data.SerializedInput;
        QueueName = data.QueueName;
        Priority = data.Priority;
        MaxRetries = data.MaxRetries;
        Status = (int)data.Status;
        AttemptCount = data.AttemptCount;
        EnqueuedAt = data.EnqueuedAt;
        ScheduledAt = data.ScheduledAt;
        LastAttemptAt = data.LastAttemptAt;
        CompletedAt = data.CompletedAt;
        LastError = data.LastError;
        MetadataJson = data.Metadata.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(data.Metadata)
            : null;
    }
}
