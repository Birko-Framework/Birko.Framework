using Birko.BackgroundJobs;
using Birko.BackgroundJobs.SQL.Models;
using FluentAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace Birko.BackgroundJobs.SQL.Tests.Models;

public class JobDescriptorModelTests
{
    private static JobDescriptor CreateTestDescriptor()
    {
        return new JobDescriptor
        {
            Id = Guid.NewGuid(),
            JobType = "TestApp.Jobs.SendEmailJob, TestApp",
            InputType = "TestApp.Models.EmailInput, TestApp",
            SerializedInput = "{\"to\":\"user@example.com\"}",
            QueueName = "emails",
            Priority = 5,
            MaxRetries = 3,
            Status = JobStatus.Pending,
            AttemptCount = 0,
            EnqueuedAt = new DateTime(2026, 3, 30, 12, 0, 0, DateTimeKind.Utc),
            ScheduledAt = new DateTime(2026, 3, 30, 13, 0, 0, DateTimeKind.Utc),
            LastAttemptAt = null,
            CompletedAt = null,
            LastError = null,
            Metadata = new Dictionary<string, string> { ["correlationId"] = "abc-123" }
        };
    }

    #region FromDescriptor / LoadFrom

    [Fact]
    public void FromDescriptor_MapsAllFields()
    {
        var descriptor = CreateTestDescriptor();

        var model = JobDescriptorModel.FromDescriptor(descriptor);

        model.Guid.Should().Be(descriptor.Id);
        model.JobType.Should().Be(descriptor.JobType);
        model.InputType.Should().Be(descriptor.InputType);
        model.SerializedInput.Should().Be(descriptor.SerializedInput);
        model.QueueName.Should().Be(descriptor.QueueName);
        model.Priority.Should().Be(descriptor.Priority);
        model.MaxRetries.Should().Be(descriptor.MaxRetries);
        model.Status.Should().Be((int)JobStatus.Pending);
        model.AttemptCount.Should().Be(0);
        model.EnqueuedAt.Should().Be(descriptor.EnqueuedAt);
        model.ScheduledAt.Should().Be(descriptor.ScheduledAt);
        model.LastAttemptAt.Should().BeNull();
        model.CompletedAt.Should().BeNull();
        model.LastError.Should().BeNull();
    }

    [Fact]
    public void FromDescriptor_MetadataJson_SerializesDict()
    {
        var descriptor = CreateTestDescriptor();

        var model = JobDescriptorModel.FromDescriptor(descriptor);

        model.MetadataJson.Should().NotBeNullOrEmpty();
        model.MetadataJson.Should().Contain("correlationId");
        model.MetadataJson.Should().Contain("abc-123");
    }

    [Fact]
    public void FromDescriptor_EmptyMetadata_SetsNull()
    {
        var descriptor = CreateTestDescriptor();
        descriptor.Metadata = new Dictionary<string, string>();

        var model = JobDescriptorModel.FromDescriptor(descriptor);

        model.MetadataJson.Should().BeNull();
    }

    #endregion

    #region ToDescriptor

    [Fact]
    public void ToDescriptor_MapsAllFields()
    {
        var original = CreateTestDescriptor();
        var model = JobDescriptorModel.FromDescriptor(original);

        var result = model.ToDescriptor();

        result.Id.Should().Be(original.Id);
        result.JobType.Should().Be(original.JobType);
        result.InputType.Should().Be(original.InputType);
        result.SerializedInput.Should().Be(original.SerializedInput);
        result.QueueName.Should().Be(original.QueueName);
        result.Priority.Should().Be(original.Priority);
        result.MaxRetries.Should().Be(original.MaxRetries);
        result.Status.Should().Be(JobStatus.Pending);
        result.AttemptCount.Should().Be(0);
        result.EnqueuedAt.Should().Be(original.EnqueuedAt);
        result.ScheduledAt.Should().Be(original.ScheduledAt);
    }

    [Fact]
    public void ToDescriptor_MetadataJson_RoundTrip()
    {
        var original = CreateTestDescriptor();
        var model = JobDescriptorModel.FromDescriptor(original);

        var result = model.ToDescriptor();

        result.Metadata.Should().ContainKey("correlationId");
        result.Metadata["correlationId"].Should().Be("abc-123");
    }

    [Fact]
    public void ToDescriptor_NullMetadataJson_ReturnsEmptyMetadata()
    {
        var model = new JobDescriptorModel
        {
            Guid = Guid.NewGuid(),
            JobType = "Test",
            MetadataJson = null
        };

        var result = model.ToDescriptor();

        result.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void ToDescriptor_NullGuid_GeneratesNew()
    {
        var model = new JobDescriptorModel
        {
            Guid = null,
            JobType = "Test"
        };

        var result = model.ToDescriptor();

        result.Id.Should().NotBe(Guid.Empty);
    }

    #endregion

    #region Status Casting

    [Fact]
    public void StatusField_CastsFromEnum()
    {
        var descriptor = CreateTestDescriptor();
        descriptor.Status = JobStatus.Processing;

        var model = JobDescriptorModel.FromDescriptor(descriptor);

        model.Status.Should().Be((int)JobStatus.Processing);
    }

    [Fact]
    public void StatusField_CastsBackToEnum()
    {
        var model = new JobDescriptorModel
        {
            Guid = Guid.NewGuid(),
            JobType = "Test",
            Status = (int)JobStatus.Completed
        };

        var result = model.ToDescriptor();

        result.Status.Should().Be(JobStatus.Completed);
    }

    #endregion
}
