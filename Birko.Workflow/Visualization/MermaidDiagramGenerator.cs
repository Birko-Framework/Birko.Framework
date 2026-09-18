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
                sb.AppendLine($"    {Escape(state.Name)} : {EscapeDescription(state.Description)}");
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

    // State descriptions are free text on the `Name : Description` line, so (unlike state
    // names/triggers) spaces are legal and must be preserved — but a newline would break the
    // single-line diagram statement and produce invalid Mermaid. Collapse CR/LF to spaces and
    // trim so the description stays on one line (CR-L403).
    private static string EscapeDescription(string value) =>
        value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
}
