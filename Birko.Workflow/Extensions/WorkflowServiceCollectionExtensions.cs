using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using Birko.Workflow.Visualization;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Workflow.Extensions;

public sealed class WorkflowEngineOptions
{
    public bool PublishStateChanges { get; set; }
}

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowEngine(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowEngine>(new WorkflowEngine());
        services.AddSingleton<IWorkflowDiagramGenerator, MermaidDiagramGenerator>();
        return services;
    }

    public static IServiceCollection AddWorkflowEngine(this IServiceCollection services, Action<WorkflowEngineOptions> configure)
    {
        var options = new WorkflowEngineOptions();
        configure(options);

        if (options.PublishStateChanges)
        {
            services.AddSingleton<IWorkflowEngine>(sp =>
            {
                return new WorkflowEngine((record, workflowName, instanceId) =>
                {
                    // If an IEventBus-like callback is registered, invoke it
                    var callbacks = sp.GetServices<Action<StateChangeRecord, string, Guid>>();
                    foreach (var callback in callbacks)
                    {
                        callback(record, workflowName, instanceId);
                    }
                });
            });
        }
        else
        {
            services.AddSingleton<IWorkflowEngine>(new WorkflowEngine());
        }

        services.AddSingleton<IWorkflowDiagramGenerator, MermaidDiagramGenerator>();
        return services;
    }
}
