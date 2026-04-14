using System;
using Birko.Data.Models;
using Birko.BackgroundJobs.Serialization;

namespace Birko.BackgroundJobs.SQL.Models
{
    /// <summary>
    /// SQL-persisted model for a background job descriptor.
    /// Maps to the "__BackgroundJobs" table via Birko.Data.SQL attributes.
    /// </summary>
    [Birko.Data.SQL.Attributes.Table("__BackgroundJobs")]
    public class JobDescriptorModel : AbstractModel, ILoadable<JobDescriptor>
    {
        [Birko.Data.SQL.Attributes.PrimaryField]
        [Birko.Data.SQL.Attributes.NamedField("Id")]
        public override Guid? Guid { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("JobType")]
        public string JobType { get; set; } = string.Empty;

        [Birko.Data.SQL.Attributes.NamedField("InputType")]
        public string? InputType { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("SerializedInput")]
        public string? SerializedInput { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("QueueName")]
        public string? QueueName { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("Priority")]
        public int Priority { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("MaxRetries")]
        public int MaxRetries { get; set; } = 3;

        [Birko.Data.SQL.Attributes.NamedField("Status")]
        public int Status { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("AttemptCount")]
        public int AttemptCount { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("EnqueuedAt")]
        public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;

        [Birko.Data.SQL.Attributes.NamedField("ScheduledAt")]
        public DateTime? ScheduledAt { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("LastAttemptAt")]
        public DateTime? LastAttemptAt { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("CompletedAt")]
        public DateTime? CompletedAt { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("LastError")]
        public string? LastError { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("Metadata")]
        public string? MetadataJson { get; set; }

        /// <summary>
        /// Converts to a core JobDescriptor.
        /// </summary>
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

        /// <summary>
        /// Creates a model from a core JobDescriptor.
        /// </summary>
        public static JobDescriptorModel FromDescriptor(JobDescriptor descriptor)
        {
            var model = new JobDescriptorModel();
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
}
