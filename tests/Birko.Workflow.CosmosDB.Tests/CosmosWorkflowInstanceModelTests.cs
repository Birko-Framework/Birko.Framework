using System;
using System.Collections.Generic;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Birko.Workflow.CosmosDB.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Workflow.CosmosDB.Tests;

/// <summary>
/// CR-M268: Birko.Workflow.CosmosDB had no test project. Covers the backend-independent model round-trip
/// (FromInstance / ToInstance / UpdateFromInstance) — Data + History survive, CreatedAt preserved and
/// UpdatedAt bumped on update, and Status enum casting.
/// </summary>
public class CosmosWorkflowInstanceModelTests
{
    private class TestData
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    private static WorkflowInstance<TestData> CreateTestInstance() =>
        WorkflowInstance<TestData>.Restore(
            Guid.NewGuid(), "Submitted", WorkflowStatus.Active,
            new TestData { OrderId = "ORD-001", Amount = 99.99m },
            new List<StateChangeRecord> { new("Draft", "Submitted", "Submit", DateTime.UtcNow) });

    [Fact]
    public void FromInstance_Then_ToInstance_RoundTrips()
    {
        var original = CreateTestInstance();
        var model = CosmosWorkflowInstanceModel.FromInstance("OrderWorkflow", original);
        model.Guid.Should().Be(original.InstanceId);
        model.WorkflowName.Should().Be("OrderWorkflow");

        var restored = model.ToInstance<TestData>();
        restored.InstanceId.Should().Be(original.InstanceId);
        restored.CurrentState.Should().Be("Submitted");
        restored.Status.Should().Be(WorkflowStatus.Active);
        restored.Data.OrderId.Should().Be("ORD-001");
        restored.Data.Amount.Should().Be(99.99m);
        restored.History.Should().ContainSingle().Which.ToState.Should().Be("Submitted");
    }

    [Fact]
    public void UpdateFromInstance_PreservesCreatedAt_BumpsUpdatedAt_UpdatesState()
    {
        var model = CosmosWorkflowInstanceModel.FromInstance("W", CreateTestInstance());
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
        model.CreatedAt.Should().Be(createdAt, "CreatedAt is preserved on update");
        model.UpdatedAt.Should().BeOnOrAfter(createdAt);

        var back = model.ToInstance<TestData>();
        back.Data.Amount.Should().Be(150m);
        back.History.Should().HaveCount(2);
    }

    [Fact]
    public void Status_CastsFromEnum()
    {
        CosmosWorkflowInstanceModel.FromInstance("W", CreateTestInstance()).Status.Should().Be((int)WorkflowStatus.Active);
    }
}
