using Birko.Workflow.Core;
using Birko.Workflow.Definition;
using Birko.Workflow.Execution;
using FluentAssertions;
using Xunit;

namespace Birko.Workflow.Tests;

public class WorkflowInstanceTests
{
    [Fact]
    public void Create_SetsInitialState()
    {
        var workflow = new WorkflowBuilder<string>("Test")
            .InitialState("Start")
            .State("Start").And()
            .State("End").IsFinal().And()
            .Transition("go", "Start", "End").And()
            .Build();

        var instance = WorkflowInstance<string>.Create(workflow, "data");

        instance.CurrentState.Should().Be("Start");
        instance.Status.Should().Be(WorkflowStatus.Active);
        instance.Data.Should().Be("data");
        instance.InstanceId.Should().NotBeEmpty();
        instance.History.Should().BeEmpty();
    }

    [Fact]
    public void Restore_RestoresFullState()
    {
        var id = Guid.NewGuid();
        var history = new[]
        {
            new StateChangeRecord("A", "B", "go", DateTime.UtcNow.AddMinutes(-5)),
            new StateChangeRecord("B", "C", "next", DateTime.UtcNow)
        };

        var instance = WorkflowInstance<string>.Restore(id, "C", WorkflowStatus.Active, "data", history);

        instance.InstanceId.Should().Be(id);
        instance.CurrentState.Should().Be("C");
        instance.Status.Should().Be(WorkflowStatus.Active);
        instance.Data.Should().Be("data");
        instance.History.Should().HaveCount(2);
    }

    [Fact]
    public void Restore_WithNullHistory_CreatesEmptyHistory()
    {
        var instance = WorkflowInstance<string>.Restore(Guid.NewGuid(), "A", WorkflowStatus.Active, "data");

        instance.History.Should().BeEmpty();
    }
}
