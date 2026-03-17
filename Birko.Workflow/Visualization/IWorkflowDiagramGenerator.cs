using Birko.Workflow.Core;

namespace Birko.Workflow.Visualization;

public interface IWorkflowDiagramGenerator
{
    string Generate<TData>(IWorkflowDefinition<TData> definition);
}
