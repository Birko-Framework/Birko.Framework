using System;
using System.Collections.Generic;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Birko.Workflow.ElasticSearch.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Workflow.ElasticSearch.Tests;

/// <summary>
/// CR-M269: Birko.Workflow.ElasticSearch had no test project. Covers the backend-independent model round-trip
/// (FromInstance / ToInstance / UpdateFromInstance) — Data + History survive, CreatedAt preserved and
/// UpdatedAt bumped on update, and Status enum casting.
/// </summary>
public class ElasticWorkflowInstanceModelTests
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
        var model = ElasticWorkflowInstanceModel.FromInstance("OrderWorkflow", original);
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
        var model = ElasticWorkflowInstanceModel.FromInstance("W", CreateTestInstance());
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
        ElasticWorkflowInstanceModel.FromInstance("W", CreateTestInstance()).Status.Should().Be((int)WorkflowStatus.Active);
    }

    [Fact]
    public void ToInstance_NullGuid_Throws()
    {
        // CR-L406: a document with no Guid must surface as a corrupt record, not mint a random
        // InstanceId that would diverge from the document and cause a duplicate on next SaveAsync.
        var model = ElasticWorkflowInstanceModel.FromInstance("W", CreateTestInstance());
        model.Guid = null;

        var act = () => model.ToInstance<TestData>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no Guid*");
    }

    [Fact]
    public void ToInstance_EmptyDataJson_ThrowsClearError()
    {
        // CR-L405: DataJson defaults to string.Empty (invalid JSON). Instead of an opaque
        // JsonException, ToInstance throws a clear InvalidOperationException naming the instance.
        var model = ElasticWorkflowInstanceModel.FromInstance("W", CreateTestInstance());
        model.DataJson = string.Empty;

        var act = () => model.ToInstance<TestData>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*empty DataJson*");
    }

    [Fact]
    public void ToInstance_LiteralNullDataJson_ThrowsClearError()
    {
        // CR-L405: a payload that legitimately deserializes to null (stored literal "null") is also
        // treated as a corrupt record rather than forced non-null via `!`.
        var model = ElasticWorkflowInstanceModel.FromInstance("W", CreateTestInstance());
        model.DataJson = "null";

        var act = () => model.ToInstance<TestData>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*deserialized to null*");
    }
}
