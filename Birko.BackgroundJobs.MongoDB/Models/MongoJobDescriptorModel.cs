using System;
using Birko.Data.Models;
using Birko.BackgroundJobs.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace Birko.BackgroundJobs.MongoDB.Models;

/// <summary>
/// MongoDB-persisted model for a background job descriptor.
/// Uses BSON attributes for document mapping.
/// </summary>
public class MongoJobDescriptorModel : AbstractModel, ILoadable<JobDescriptor>
{
    [BsonElement("jobType")]
    public string JobType { get; set; } = string.Empty;

    [BsonElement("inputType")]
    public string? InputType { get; set; }

    [BsonElement("serializedInput")]
    public string? SerializedInput { get; set; }

    [BsonElement("queueName")]
    public string? QueueName { get; set; }

    [BsonElement("priority")]
    public int Priority { get; set; }

    [BsonElement("maxRetries")]
    public int MaxRetries { get; set; } = 3;

    [BsonElement("status")]
    public int Status { get; set; }

    [BsonElement("attemptCount")]
    public int AttemptCount { get; set; }

    [BsonElement("enqueuedAt")]
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("scheduledAt")]
    public DateTime? ScheduledAt { get; set; }

    [BsonElement("lastAttemptAt")]
    public DateTime? LastAttemptAt { get; set; }

    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [BsonElement("lastError")]
    public string? LastError { get; set; }

    [BsonElement("metadataJson")]
    public string? MetadataJson { get; set; }

    [BsonIgnore]
    public string CollectionName => "BackgroundJobs";

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
            var metadata = JobSerializationHelper.DeserializeMetadata(MetadataJson);
            if (metadata != null)
            {
                descriptor.Metadata = metadata;
            }
        }

        return descriptor;
    }

    public static MongoJobDescriptorModel FromDescriptor(JobDescriptor descriptor)
    {
        var model = new MongoJobDescriptorModel();
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
        MetadataJson = JobSerializationHelper.SerializeMetadata(data.Metadata);
    }
}
