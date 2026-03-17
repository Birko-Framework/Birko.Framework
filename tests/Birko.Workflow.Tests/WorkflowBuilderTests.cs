using Birko.Workflow.Definition;
using FluentAssertions;
using Xunit;

namespace Birko.Workflow.Tests;

public class WorkflowBuilderTests
{
    [Fact]
    public void Build_WithValidDefinition_CreatesWorkflow()
    {
        var workflow = new WorkflowBuilder<string>("Test")
            .InitialState("A")
            .State("A").And()
            .State("B").IsFinal().And()
            .Transition("go", "A", "B").And()
            .Build();

        workflow.Name.Should().Be("Test");
        workflow.InitialState.Should().Be("A");
        workflow.States.Should().HaveCount(2);
        workflow.Transitions.Should().HaveCount(1);
    }

    [Fact]
    public void Build_WithoutInitialState_Throws()
    {
        var builder = new WorkflowBuilder<string>("Test")
            .State("A").And();

        var act = () => builder.Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*InitialState*");
    }

    [Fact]
    public void Build_WithUndefinedInitialState_Throws()
    {
        var builder = new WorkflowBuilder<string>("Test")
            .InitialState("Missing")
            .State("A").And();

        var act = () => builder.Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*'Missing'*not defined*");
    }

    [Fact]
    public void Build_WithFinalInitialState_Throws()
    {
        var builder = new WorkflowBuilder<string>("Test")
            .InitialState("A")
            .State("A").IsFinal().And();

        var act = () => builder.Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot be a final state*");
    }

    [Fact]
    public void Build_WithUndefinedFromState_Throws()
    {
        var builder = new WorkflowBuilder<string>("Test")
            .InitialState("A")
            .State("A").And()
            .State("B").And()
            .Transition("go", "X", "B").And();

        var act = () => builder.Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*undefined FromState 'X'*");
    }

    [Fact]
    public void Build_WithUndefinedToState_Throws()
    {
        var builder = new WorkflowBuilder<string>("Test")
            .InitialState("A")
            .State("A").And()
            .Transition("go", "A", "X").And();

        var act = () => builder.Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*undefined ToState 'X'*");
    }

    [Fact]
    public void Build_WithEmptyName_Throws()
    {
        var act = () => new WorkflowBuilder<string>("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_WithStateDescription_SetsDescription()
    {
        var workflow = new WorkflowBuilder<string>("Test")
            .InitialState("A")
            .State("A").Description("Start state").And()
            .State("B").IsFinal().And()
            .Transition("go", "A", "B").And()
            .Build();

        workflow.States[0].Description.Should().Be("Start state");
    }

    [Fact]
    public void GetPermittedTriggers_ReturnsMatchingTriggers()
    {
        var workflow = new WorkflowBuilder<string>("Test")
            .InitialState("A")
            .State("A").And()
            .State("B").And()
            .State("C").IsFinal().And()
            .Transition("go", "A", "B").And()
            .Transition("skip", "A", "C").And()
            .Transition("finish", "B", "C").And()
            .Build();

        workflow.GetPermittedTriggers("A").Should().BeEquivalentTo("go", "skip");
        workflow.GetPermittedTriggers("B").Should().BeEquivalentTo("finish");
        workflow.GetPermittedTriggers("C").Should().BeEmpty();
    }

    [Fact]
    public void Build_MultipleSameTrigger_DifferentFromStates_Allowed()
    {
        var workflow = new WorkflowBuilder<string>("Test")
            .InitialState("A")
            .State("A").And()
            .State("B").And()
            .State("C").IsFinal().And()
            .Transition("cancel", "A", "C").And()
            .Transition("cancel", "B", "C").And()
            .Transition("next", "A", "B").And()
            .Build();

        workflow.Transitions.Should().HaveCount(3);
    }
}
