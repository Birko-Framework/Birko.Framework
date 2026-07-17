using System;
using System.Collections.Generic;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Birko.Workflow.JSON.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Workflow.JSON.Tests;

/// <summary>
/// CR-M270: Birko.Workflow.JSON had no test project. Covers the backend-independent model round-trip
/// (FromInstance / ToInstance / UpdateFromInstance) incl. Data + History JSON serialization, the
/// CreatedAt-preserved-on-update behavior, empty-history, and Status enum casting.
/// </summary>
public class JsonWorkflowInstanceModelTests
{
    private class TestData
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    private static WorkflowInstance<TestData> CreateTestInstance() =>
        WorkflowInstance<TestData>.Restore(
            Guid.NewGuid(),
            "Submitted",
            WorkflowStatus.Active,
            new TestData { OrderId = "ORD-001", Amount = 99.99m },
            new List<StateChangeRecord> { new("Draft", "Submitted", "Submit", DateTime.UtcNow) });

    [Fact]
    public void FromInstance_MapsAllFields()
    {
        var instance = CreateTestInstance();
        var model = JsonWorkflowInstanceModel.FromInstance("OrderWorkflow", instance);

        model.Guid.Should().Be(instance.InstanceId);
        model.WorkflowName.Should().Be("OrderWorkflow");
        model.CurrentState.Should().Be("Submitted");
        model.Status.Should().Be((int)WorkflowStatus.Active);
        model.DataJson.Should().Contain("ORD-001").And.Contain("99.99");
        model.HistoryJson.Should().Contain("Submitted");
    }

    [Fact]
    public void ToInstance_RoundTripsDataAndHistory()
    {
        var original = CreateTestInstance();
        var restored = JsonWorkflowInstanceModel.FromInstance("W", original).ToInstance<TestData>();

        restored.InstanceId.Should().Be(original.InstanceId);
        restored.CurrentState.Should().Be("Submitted");
        restored.Status.Should().Be(WorkflowStatus.Active);
        restored.Data.OrderId.Should().Be("ORD-001");
        restored.Data.Amount.Should().Be(99.99m);
        restored.History.Should().ContainSingle().Which.ToState.Should().Be("Submitted");
    }

    [Fact]
    public void ToInstance_EmptyHistory_ReturnsEmptyList()
    {
        var model = new JsonWorkflowInstanceModel
        {
            Guid = Guid.NewGuid(),
            CurrentState = "Initial",
            Status = (int)WorkflowStatus.NotStarted,
            DataJson = "{\"OrderId\":\"X\",\"Amount\":0}",
            HistoryJson = "[]",
        };

        model.ToInstance<TestData>().History.Should().BeEmpty();
    }

    [Fact]
    public void UpdateFromInstance_UpdatesFields_PreservesCreatedAt_BumpsUpdatedAt()
    {
        var model = JsonWorkflowInstanceModel.FromInstance("W", CreateTestInstance());
        var createdAt = model.CreatedAt;

        var updated = WorkflowInstance<TestData>.Restore(
            model.Guid!.Value, "Approved", WorkflowStatus.Completed,
            new TestData { OrderId = "ORD-001", Amount = 150m },
            new List<StateChangeRecord>
            {
                new("Draft", "Submitted", "Submit", DateTime.UtcNow),
                new("Submitted", "Approved", "Approve", DateTime.UtcNow),
            });

        model.UpdateFromInstance(updated);

        model.CurrentState.Should().Be("Approved");
        model.Status.Should().Be((int)WorkflowStatus.Completed);
        model.DataJson.Should().Contain("150");
        model.HistoryJson.Should().Contain("Approved");
        model.CreatedAt.Should().Be(createdAt, "CreatedAt is preserved on update");
        model.UpdatedAt.Should().BeOnOrAfter(createdAt);
    }

    [Fact]
    public void Status_CastsBothWays()
    {
        JsonWorkflowInstanceModel.FromInstance("W", CreateTestInstance()).Status.Should().Be((int)WorkflowStatus.Active);

        var model = new JsonWorkflowInstanceModel
        {
            Guid = Guid.NewGuid(),
            CurrentState = "Done",
            Status = (int)WorkflowStatus.Completed,
            DataJson = "{\"OrderId\":\"\",\"Amount\":0}",
            HistoryJson = "[]",
        };
        model.ToInstance<TestData>().Status.Should().Be(WorkflowStatus.Completed);
    }

    [Fact]
    public void ToInstance_EmptyDataJson_ThrowsClearError()
    {
        // CR-L408: DataJson defaults to string.Empty (invalid JSON). Rather than an opaque
        // deserializer exception or a `!`-forced null, ToInstance throws a clear InvalidOperationException.
        var model = JsonWorkflowInstanceModel.FromInstance("W", CreateTestInstance());
        model.DataJson = string.Empty;

        var act = () => model.ToInstance<TestData>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*empty DataJson*");
    }

    [Fact]
    public void ToInstance_LiteralNullDataJson_ThrowsClearError()
    {
        // CR-L408: a payload that deserializes to null (stored literal "null") is treated as a corrupt
        // record instead of being forced non-null via `!` and deferring a NullReferenceException.
        var model = JsonWorkflowInstanceModel.FromInstance("W", CreateTestInstance());
        model.DataJson = "null";

        var act = () => model.ToInstance<TestData>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*deserialized to null*");
    }
}
