using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Birko.Workflow.SQL.Models;
using FluentAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace Birko.Workflow.SQL.Tests.Models;

public class WorkflowInstanceModelTests
{
    private class TestData
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    private static WorkflowInstance<TestData> CreateTestInstance()
    {
        var data = new TestData { OrderId = "ORD-001", Amount = 99.99m };
        var history = new List<StateChangeRecord>
        {
            new("Draft", "Submitted", "Submit", DateTime.UtcNow)
        };

        return WorkflowInstance<TestData>.Restore(
            Guid.NewGuid(),
            "Submitted",
            WorkflowStatus.Active,
            data,
            history);
    }

    #region FromInstance

    [Fact]
    public void FromInstance_MapsAllFields()
    {
        var instance = CreateTestInstance();

        var model = WorkflowInstanceModel.FromInstance("OrderWorkflow", instance);

        model.Guid.Should().Be(instance.InstanceId);
        model.WorkflowName.Should().Be("OrderWorkflow");
        model.CurrentState.Should().Be("Submitted");
        model.Status.Should().Be((int)WorkflowStatus.Active);
        model.DataJson.Should().Contain("ORD-001");
        model.DataJson.Should().Contain("99.99");
        model.HistoryJson.Should().Contain("Submitted");
    }

    #endregion

    #region ToInstance

    [Fact]
    public void ToInstance_DeserializesDataAndHistory()
    {
        var original = CreateTestInstance();
        var model = WorkflowInstanceModel.FromInstance("OrderWorkflow", original);

        var restored = model.ToInstance<TestData>();

        restored.InstanceId.Should().Be(original.InstanceId);
        restored.CurrentState.Should().Be("Submitted");
        restored.Status.Should().Be(WorkflowStatus.Active);
        restored.Data.OrderId.Should().Be("ORD-001");
        restored.Data.Amount.Should().Be(99.99m);
        restored.History.Should().HaveCount(1);
        restored.History[0].ToState.Should().Be("Submitted");
    }

    [Fact]
    public void ToInstance_EmptyHistory_ReturnsEmptyList()
    {
        var model = new WorkflowInstanceModel
        {
            Guid = Guid.NewGuid(),
            WorkflowName = "Test",
            CurrentState = "Initial",
            Status = (int)WorkflowStatus.NotStarted,
            DataJson = "{\"OrderId\":\"X\",\"Amount\":0}",
            HistoryJson = "[]"
        };

        var result = model.ToInstance<TestData>();

        result.History.Should().BeEmpty();
    }

    #endregion

    #region UpdateFromInstance

    [Fact]
    public void UpdateFromInstance_UpdatesFieldsAndTimestamp()
    {
        var instance = CreateTestInstance();
        var model = WorkflowInstanceModel.FromInstance("OrderWorkflow", instance);
        var originalUpdatedAt = model.UpdatedAt;

        // Simulate state change
        var updatedData = new TestData { OrderId = "ORD-001", Amount = 150m };
        var updatedHistory = new List<StateChangeRecord>
        {
            new("Draft", "Submitted", "Submit", DateTime.UtcNow),
            new("Submitted", "Approved", "Approve", DateTime.UtcNow)
        };
        var updatedInstance = WorkflowInstance<TestData>.Restore(
            instance.InstanceId, "Approved", WorkflowStatus.Active, updatedData, updatedHistory);

        model.UpdateFromInstance(updatedInstance);

        model.CurrentState.Should().Be("Approved");
        model.DataJson.Should().Contain("150");
        model.HistoryJson.Should().Contain("Approved");
        model.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    #endregion

    #region DataJson Round-Trip

    [Fact]
    public void DataJson_RoundTrip_PreservesValues()
    {
        var instance = CreateTestInstance();
        var model = WorkflowInstanceModel.FromInstance("Test", instance);
        var restored = model.ToInstance<TestData>();

        restored.Data.OrderId.Should().Be(instance.Data.OrderId);
        restored.Data.Amount.Should().Be(instance.Data.Amount);
    }

    #endregion

    #region Status Casting

    [Fact]
    public void StatusField_CastsFromEnum()
    {
        var instance = CreateTestInstance();
        var model = WorkflowInstanceModel.FromInstance("Test", instance);

        model.Status.Should().Be((int)WorkflowStatus.Active);
    }

    [Fact]
    public void StatusField_CastsBackToEnum()
    {
        var model = new WorkflowInstanceModel
        {
            Guid = Guid.NewGuid(),
            CurrentState = "Done",
            Status = (int)WorkflowStatus.Completed,
            DataJson = "{\"OrderId\":\"\",\"Amount\":0}",
            HistoryJson = "[]"
        };

        var result = model.ToInstance<TestData>();

        result.Status.Should().Be(WorkflowStatus.Completed);
    }

    #endregion

    #region Corrupt-record guards (CR-L415)

    [Fact]
    public void ToInstance_EmptyDataJson_ThrowsClearError()
    {
        // CR-L415: DataJson defaults to string.Empty (invalid JSON) — ToInstance throws a clear
        // InvalidOperationException instead of `!`-forcing a null into Restore.
        var model = WorkflowInstanceModel.FromInstance("W", CreateTestInstance());
        model.DataJson = string.Empty;

        var act = () => model.ToInstance<TestData>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*empty DataJson*");
    }

    [Fact]
    public void ToInstance_LiteralNullDataJson_ThrowsClearError()
    {
        // CR-L415: a payload that deserializes to null (stored literal "null") is treated as a corrupt
        // record rather than deferring a NullReferenceException to instance.Data consumers.
        var model = WorkflowInstanceModel.FromInstance("W", CreateTestInstance());
        model.DataJson = "null";

        var act = () => model.ToInstance<TestData>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*deserialized to null*");
    }

    [Fact]
    public void FromInstance_PreservesPascalCaseWireFormat()
    {
        // CR-L416: routing through ISerializer must NOT change the persisted wire format. The backend
        // historically wrote PascalCase (default System.Text.Json) DataJson; SystemJsonSerializer's
        // parameterless default is camelCase, which would silently fail to match existing rows on read.
        // Pin PascalCase property names so a future camelCase regression is caught.
        var model = WorkflowInstanceModel.FromInstance("W", CreateTestInstance());

        model.DataJson.Should().Contain("\"OrderId\"");
        model.DataJson.Should().NotContain("\"orderId\"");
    }

    #endregion
}
