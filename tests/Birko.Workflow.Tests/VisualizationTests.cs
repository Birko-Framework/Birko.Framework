using Birko.Workflow.Definition;
using Birko.Workflow.Visualization;
using FluentAssertions;
using Xunit;

namespace Birko.Workflow.Tests;

public class VisualizationTests
{
    private static WorkflowDefinition<string> CreateSimpleWorkflow()
    {
        return new WorkflowBuilder<string>("TestFlow")
            .InitialState("A")
            .State("A").Description("Start").And()
            .State("B").And()
            .State("C").IsFinal().And()
            .Transition("go", "A", "B").And()
            .Transition("finish", "B", "C").And()
            .Build();
    }

    [Fact]
    public void MermaidGenerator_ProducesValidOutput()
    {
        var generator = new MermaidDiagramGenerator();
        var output = generator.Generate(CreateSimpleWorkflow());

        output.Should().Contain("stateDiagram-v2");
        output.Should().Contain("[*] --> A");
        output.Should().Contain("A : Start");
        output.Should().Contain("A --> B : go");
        output.Should().Contain("B --> C : finish");
        output.Should().Contain("C --> [*]");
    }

    [Fact]
    public void DotGenerator_ProducesValidOutput()
    {
        var generator = new DotDiagramGenerator();
        var output = generator.Generate(CreateSimpleWorkflow());

        output.Should().Contain("digraph \"TestFlow\"");
        output.Should().Contain("rankdir=LR");
        output.Should().Contain("__start__");
        output.Should().Contain("__start__ -> \"A\"");
        output.Should().Contain("\"A\" -> \"B\" [label=\"go\"]");
        output.Should().Contain("\"B\" -> \"C\" [label=\"finish\"]");
        output.Should().Contain("doublecircle");
    }
}
