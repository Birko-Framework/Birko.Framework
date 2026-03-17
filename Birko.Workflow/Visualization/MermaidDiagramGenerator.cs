using System.Text;
using Birko.Workflow.Core;

namespace Birko.Workflow.Visualization;

public sealed class MermaidDiagramGenerator : IWorkflowDiagramGenerator
{
    public string Generate<TData>(IWorkflowDefinition<TData> definition)
    {
        var sb = new StringBuilder();
        sb.AppendLine("stateDiagram-v2");

        sb.AppendLine($"    [*] --> {Escape(definition.InitialState)}");

        foreach (var state in definition.States)
        {
            if (state.Description != null)
            {
                sb.AppendLine($"    {Escape(state.Name)} : {state.Description}");
            }
        }

        foreach (var transition in definition.Transitions)
        {
            sb.AppendLine($"    {Escape(transition.FromState)} --> {Escape(transition.ToState)} : {Escape(transition.Trigger)}");
        }

        foreach (var state in definition.States.Where(s => s.IsFinal))
        {
            sb.AppendLine($"    {Escape(state.Name)} --> [*]");
        }

        return sb.ToString().TrimEnd();
    }

    private static string Escape(string value) => value.Replace(" ", "_");
}
