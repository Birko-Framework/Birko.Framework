using System;
using System.Xml.Serialization;
using Birko.Data.Models;
using Birko.Serialization;
using Birko.Serialization.Xml;

namespace Birko.BackgroundJobs.XML.Models;

/// <summary>
/// XML file-persisted model for a background job descriptor.
/// Uses System.Xml.Serialization attributes for serialization.
/// </summary>
[XmlRoot("JobDescriptor")]
public class XmlJobDescriptorModel : AbstractModel, ILoadable<JobDescriptor>
{
    [XmlElement("JobType")]
    public string JobType { get; set; } = string.Empty;

    [XmlElement("InputType")]
    public string? InputType { get; set; }

    [XmlElement("SerializedInput")]
    public string? SerializedInput { get; set; }

    [XmlElement("QueueName")]
    public string? QueueName { get; set; }

    [XmlElement("Priority")]
    public int Priority { get; set; }

    [XmlElement("MaxRetries")]
    public int MaxRetries { get; set; } = 3;

    [XmlElement("Status")]
    public int Status { get; set; }

    [XmlElement("AttemptCount")]
    public int AttemptCount { get; set; }

    [XmlElement("EnqueuedAt")]
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;

    [XmlElement("ScheduledAt", IsNullable = true)]
    public DateTime? ScheduledAt { get; set; }

    [XmlElement("LastAttemptAt", IsNullable = true)]
    public DateTime? LastAttemptAt { get; set; }

    [XmlElement("CompletedAt", IsNullable = true)]
    public DateTime? CompletedAt { get; set; }

    [XmlElement("LastError")]
    public string? LastError { get; set; }

    [XmlElement("MetadataXml")]
    public string? MetadataXml { get; set; }

    private static readonly ISerializer DefaultSerializer = new SystemXmlSerializer();

    public JobDescriptor ToDescriptor(ISerializer? serializer = null)
    {
        var s = serializer ?? DefaultSerializer;
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

        if (!string.IsNullOrEmpty(MetadataXml))
        {
            var metadata = s.Deserialize<SerializableMetadata>(MetadataXml);
            if (metadata?.Entries != null)
            {
                foreach (var entry in metadata.Entries)
                {
                    descriptor.Metadata[entry.Key] = entry.Value;
                }
            }
        }

        return descriptor;
    }

    public static XmlJobDescriptorModel FromDescriptor(JobDescriptor descriptor)
    {
        var model = new XmlJobDescriptorModel();
        model.LoadFrom(descriptor);
        return model;
    }

    public void LoadFrom(JobDescriptor data)
    {
        LoadFrom(data, null);
    }

    public void LoadFrom(JobDescriptor data, ISerializer? serializer)
    {
        var s = serializer ?? DefaultSerializer;
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

        if (data.Metadata.Count > 0)
        {
            var payload = new SerializableMetadata();
            foreach (var kvp in data.Metadata)
            {
                payload.Entries.Add(new MetadataEntry { Key = kvp.Key, Value = kvp.Value });
            }
            MetadataXml = s.Serialize(payload);
        }
        else
        {
            MetadataXml = null;
        }
    }
}

/// <summary>
/// XML-serializable wrapper for the job metadata dictionary.
/// System.Xml.Serialization does not support Dictionary&lt;TKey, TValue&gt; directly.
/// </summary>
[XmlRoot("Metadata")]
public class SerializableMetadata
{
    [XmlElement("Entry")]
    public System.Collections.Generic.List<MetadataEntry> Entries { get; set; } = new();
}

/// <summary>
/// Single key/value entry for <see cref="SerializableMetadata"/>.
/// </summary>
public class MetadataEntry
{
    [XmlAttribute("key")]
    public string Key { get; set; } = string.Empty;

    [XmlText]
    public string Value { get; set; } = string.Empty;
}
