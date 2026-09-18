using System;
using System.Collections.Generic;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Birko.Workflow.XML.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Workflow.XML.Tests;

/// <summary>
/// CR-L418: Birko.Workflow.XML had no dedicated test project. Covers the backend-independent model
/// round-trip (FromInstance / ToInstance / UpdateFromInstance) via SystemXmlSerializer, plus the
/// empty-history HistoryXml default that CR-M275 fixed.
/// </summary>
public class XmlWorkflowInstanceModelTests
{
    // XmlSerializer requires a public parameterless ctor + settable properties, so the payload type
    // used here is a plain POCO (records/positional types are not XmlSerializer-friendly).
    public class TestData
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    private static WorkflowInstance<TestData> CreateInstance(IEnumerable<StateChangeRecord>? history = null) =>
        WorkflowInstance<TestData>.Restore(
            Guid.NewGuid(), "Submitted", WorkflowStatus.Active,
            new TestData { OrderId = "ORD-001", Amount = 99.99m },
            history ?? new List<StateChangeRecord>());

    [Fact]
    public void FromInstance_MapsScalarFields()
    {
        var instance = CreateInstance();
        var model = XmlWorkflowInstanceModel.FromInstance("OrderWorkflow", instance);

        model.Guid.Should().Be(instance.InstanceId);
        model.WorkflowName.Should().Be("OrderWorkflow");
        model.CurrentState.Should().Be("Submitted");
        model.Status.Should().Be((int)WorkflowStatus.Active);
    }

    [Fact]
    public void FromInstance_Then_ToInstance_RoundTripsDataAndEmptyHistory()
    {
        var original = CreateInstance();
        var model = XmlWorkflowInstanceModel.FromInstance("W", original);

        var restored = model.ToInstance<TestData>();

        restored.InstanceId.Should().Be(original.InstanceId);
        restored.CurrentState.Should().Be("Submitted");
        restored.Status.Should().Be(WorkflowStatus.Active);
        restored.Data.OrderId.Should().Be("ORD-001");
        restored.Data.Amount.Should().Be(99.99m);
        restored.History.Should().BeEmpty();
    }

    [Fact]
    public void ToInstance_DefaultHistoryXml_YieldsEmptyHistory()
    {
        // CR-M275 / CR-L418: the HistoryXml default "<ArrayOfXmlStateChangeRecord />" (the DTO's empty
        // array root) must deserialize to an empty list (an unmatched root throws "<...> was not expected").
        var model = XmlWorkflowInstanceModel.FromInstance("W", CreateInstance());
        model.HistoryXml = "<ArrayOfXmlStateChangeRecord />";

        model.ToInstance<TestData>().History.Should().BeEmpty();
    }

    [Fact]
    public void UpdateFromInstance_UpdatesStateAndData()
    {
        var model = XmlWorkflowInstanceModel.FromInstance("W", CreateInstance());

        var updated = WorkflowInstance<TestData>.Restore(
            model.Guid!.Value, "Approved", WorkflowStatus.Completed,
            new TestData { OrderId = "ORD-001", Amount = 150m },
            new List<StateChangeRecord>());
        model.UpdateFromInstance(updated);

        model.CurrentState.Should().Be("Approved");
        model.Status.Should().Be((int)WorkflowStatus.Completed);

        var back = model.ToInstance<TestData>();
        back.Data.Amount.Should().Be(150m);
    }

    [Fact]
    public void Status_CastsFromEnum()
    {
        XmlWorkflowInstanceModel.FromInstance("W", CreateInstance()).Status.Should().Be((int)WorkflowStatus.Active);
    }

    [Fact]
    public void FromInstance_Then_ToInstance_RoundTripsNonEmptyHistory()
    {
        // CR-L418: proves the backend can now persist a NON-empty history. Before the DTO fix,
        // FromInstance threw (IReadOnlyList interface + positional-record StateChangeRecord are not
        // XmlSerializer-serializable), so the XML backend could never save a real workflow instance.
        var occurred = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc);
        var original = CreateInstance(new List<StateChangeRecord>
        {
            new("Draft", "Submitted", "Submit", occurred),
            new("Submitted", "Approved", "Approve", occurred.AddMinutes(5)),
        });

        var model = XmlWorkflowInstanceModel.FromInstance("W", original);
        var restored = model.ToInstance<TestData>();

        restored.History.Should().HaveCount(2);
        restored.History[0].FromState.Should().Be("Draft");
        restored.History[0].ToState.Should().Be("Submitted");
        restored.History[0].Trigger.Should().Be("Submit");
        restored.History[0].OccurredAt.Should().Be(occurred);
        restored.History[1].ToState.Should().Be("Approved");
    }

    // ── STORY-029: corrupt-record guards (null Guid + empty DataXml) ──

    [Fact]
    public void ToInstance_NullGuid_Throws()
    {
        var model = XmlWorkflowInstanceModel.FromInstance("W", CreateInstance());
        model.Guid = null;

        var act = () => model.ToInstance<TestData>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no Guid*");
    }

    [Fact]
    public void ToInstance_EmptyDataXml_ThrowsClearError()
    {
        var model = XmlWorkflowInstanceModel.FromInstance("W", CreateInstance());
        model.DataXml = string.Empty;

        var act = () => model.ToInstance<TestData>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*empty DataXml*");
    }
}
