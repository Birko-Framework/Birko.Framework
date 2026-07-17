using System;
using System.Collections.Generic;
using System.Text.Json;
using Birko.Serialization.Json;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Birko.Workflow.RavenDB.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Workflow.RavenDB.Tests;

/// <summary>
/// CR-M272: Birko.Workflow.RavenDB had no test project. Covers the backend-independent model round-trip
/// (FromInstance / ToInstance / UpdateFromInstance) — Data + History survive, CreatedAt preserved and
/// UpdatedAt bumped on update, and Status enum casting.
/// </summary>
public class RavenWorkflowInstanceModelTests
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
        var model = RavenWorkflowInstanceModel.FromInstance("OrderWorkflow", original);
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
        var model = RavenWorkflowInstanceModel.FromInstance("W", CreateTestInstance());
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
        RavenWorkflowInstanceModel.FromInstance("W", CreateTestInstance()).Status.Should().Be((int)WorkflowStatus.Active);
    }

    [Fact]
    public void ToInstance_EmptyDataJson_ThrowsClearError()
    {
        // CR-L413: DataJson defaults to string.Empty (invalid JSON) — ToInstance throws a clear
        // InvalidOperationException instead of `!`-forcing a null into Restore.
        var model = RavenWorkflowInstanceModel.FromInstance("W", CreateTestInstance());
        model.DataJson = string.Empty;

        var act = () => model.ToInstance<TestData>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*empty DataJson*");
    }

    [Fact]
    public void ToInstance_LiteralNullDataJson_ThrowsClearError()
    {
        // CR-L413: a payload that deserializes to null (stored literal "null") is treated as a corrupt
        // record rather than deferring a NullReferenceException to instance.Data consumers.
        var model = RavenWorkflowInstanceModel.FromInstance("W", CreateTestInstance());
        model.DataJson = "null";

        var act = () => model.ToInstance<TestData>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*deserialized to null*");
    }

    // ── STORY-029: ISerializer seam + camelCase wire format + null-Guid guard ──

    [Fact]
    public void FromInstance_UsesCamelCaseWireFormat()
    {
        var model = RavenWorkflowInstanceModel.FromInstance("W", CreateTestInstance());

        model.DataJson.Should().Contain("\"orderId\"");
        model.DataJson.Should().NotContain("\"OrderId\"");
    }

    [Fact]
    public void FromInstance_WithInjectedSerializer_OverridesFormat()
    {
        var pascal = new SystemJsonSerializer(new JsonSerializerOptions());
        var model = RavenWorkflowInstanceModel.FromInstance("W", CreateTestInstance(), pascal);

        model.DataJson.Should().Contain("\"OrderId\"");
    }

    [Fact]
    public void ToInstance_NullGuid_Throws()
    {
        var model = RavenWorkflowInstanceModel.FromInstance("W", CreateTestInstance());
        model.Guid = null;

        var act = () => model.ToInstance<TestData>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no Guid*");
    }
}
