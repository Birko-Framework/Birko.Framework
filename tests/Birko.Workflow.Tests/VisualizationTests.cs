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
    public void MermaidGenerator_DescriptionWithNewline_StaysSingleLine()
    {
        // CR-L403: a state description is free text on the `Name : Description` line. A newline
        // in it would split the single-line diagram statement and produce invalid Mermaid, so
        // CR/LF are collapsed to spaces (state names/triggers still go through the name escape).
        var workflow = new WorkflowBuilder<string>("DescFlow")
            .InitialState("A")
            .State("A").Description("Line one\r\nLine two\nLine three").And()
            .State("B").IsFinal().And()
            .Transition("go", "A", "B").And()
            .Build();

        var generator = new MermaidDiagramGenerator();
        var output = generator.Generate(workflow);

        output.Should().Contain("A : Line one Line two Line three");
        // The description line must not introduce a raw newline into the statement.
        output.Should().NotContain("Line one\r");
        output.Should().NotContain("Line one\n");
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
