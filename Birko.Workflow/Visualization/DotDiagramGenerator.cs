using System.Text;
using Birko.Workflow.Core;

namespace Birko.Workflow.Visualization;

public sealed class DotDiagramGenerator : IWorkflowDiagramGenerator
{
    public string Generate<TData>(IWorkflowDefinition<TData> definition)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"digraph \"{Escape(definition.Name)}\" {{");
        sb.AppendLine("    rankdir=LR;");
        sb.AppendLine("    node [shape=rectangle, style=rounded];");
        sb.AppendLine();

        sb.AppendLine("    __start__ [shape=point, width=0.2];");
        sb.AppendLine($"    __start__ -> \"{Escape(definition.InitialState)}\";");
        sb.AppendLine();

        foreach (var state in definition.States)
        {
            var label = state.Description != null
                ? $"{state.Name}\\n{state.Description}"
                : state.Name;

            var attrs = state.IsFinal
                ? $"[label=\"{Escape(label)}\", shape=doublecircle]"
                : $"[label=\"{Escape(label)}\"]";

            sb.AppendLine($"    \"{Escape(state.Name)}\" {attrs};");
        }

        sb.AppendLine();

        foreach (var transition in definition.Transitions)
        {
            sb.AppendLine($"    \"{Escape(transition.FromState)}\" -> \"{Escape(transition.ToState)}\" [label=\"{Escape(transition.Trigger)}\"];");
        }

        sb.AppendLine("}");

        return sb.ToString().TrimEnd();
    }

    private static string Escape(string value) => value.Replace("\"", "\\\"");
}
