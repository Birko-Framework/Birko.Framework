using System;
using System.Collections.Generic;
using System.Text.Json;
using Birko.Data.Models;

namespace Birko.BackgroundJobs.CosmosDB.Models;

/// <summary>
/// Cosmos DB-persisted model for a background job descriptor.
/// </summary>
public class CosmosJobDescriptorModel : AbstractModel, ILoadable<JobDescriptor>
{
    public string JobType { get; set; } = string.Empty;
    public string? InputType { get; set; }
    public string? SerializedInput { get; set; }
    public string? QueueName { get; set; }
    public int Priority { get; set; }
    public int MaxRetries { get; set; } = 3;
    public int Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ScheduledAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? LastError { get; set; }
    public string? MetadataJson { get; set; }

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
            var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(MetadataJson);
            if (metadata != null)
            {
                descriptor.Metadata = metadata;
            }
        }

        return descriptor;
    }

    public static CosmosJobDescriptorModel FromDescriptor(JobDescriptor descriptor)
    {
        var model = new CosmosJobDescriptorModel();
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
            ? JsonSerializer.Serialize(data.Metadata)
            : null;
    }
}
