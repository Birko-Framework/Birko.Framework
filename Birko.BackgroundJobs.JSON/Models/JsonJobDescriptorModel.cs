using System;
using Birko.Data.Models;
using System.Text.Json.Serialization;

namespace Birko.BackgroundJobs.JSON.Models;

/// <summary>
/// JSON file-persisted model for a background job descriptor.
/// Uses System.Text.Json attributes for serialization.
/// </summary>
public class JsonJobDescriptorModel : AbstractModel, ILoadable<JobDescriptor>
{
    [JsonPropertyName("jobType")]
    public string JobType { get; set; } = string.Empty;

    [JsonPropertyName("inputType")]
    public string? InputType { get; set; }

    [JsonPropertyName("serializedInput")]
    public string? SerializedInput { get; set; }

    [JsonPropertyName("queueName")]
    public string? QueueName { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("attemptCount")]
    public int AttemptCount { get; set; }

    [JsonPropertyName("enqueuedAt")]
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("scheduledAt")]
    public DateTime? ScheduledAt { get; set; }

    [JsonPropertyName("lastAttemptAt")]
    public DateTime? LastAttemptAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("lastError")]
    public string? LastError { get; set; }

    [JsonPropertyName("metadataJson")]
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
            var metadata = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(MetadataJson);
            if (metadata != null)
            {
                descriptor.Metadata = metadata;
            }
        }

        return descriptor;
    }

    public static JsonJobDescriptorModel FromDescriptor(JobDescriptor descriptor)
    {
        var model = new JsonJobDescriptorModel();
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
