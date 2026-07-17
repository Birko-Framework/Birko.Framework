using Birko.Workflow.Core;
using Birko.Workflow.Definition;
using Birko.Workflow.Execution;
using Birko.Workflow.Extensions;
using Birko.Workflow.Visualization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Workflow.Tests;

public class DiExtensionTests
{
    [Fact]
    public void AddWorkflowEngine_RegistersEngine()
    {
        var services = new ServiceCollection();
        services.AddWorkflowEngine();
        var provider = services.BuildServiceProvider();

        var engine = provider.GetService<IWorkflowEngine>();
        engine.Should().NotBeNull();
    }

    [Fact]
    public void AddWorkflowEngine_RegistersDiagramGenerator()
    {
        var services = new ServiceCollection();
        services.AddWorkflowEngine();
        var provider = services.BuildServiceProvider();

        var generator = provider.GetService<IWorkflowDiagramGenerator>();
        generator.Should().NotBeNull();
        generator.Should().BeOfType<MermaidDiagramGenerator>();
    }

    [Fact]
    public void AddWorkflowEngine_WithOptions_RegistersEngine()
    {
        var services = new ServiceCollection();
        services.AddWorkflowEngine(options => options.PublishStateChanges = true);
        var provider = services.BuildServiceProvider();

        var engine = provider.GetService<IWorkflowEngine>();
        engine.Should().NotBeNull();
    }

    [Fact]
    public async Task AddWorkflowEngine_PublishStateChanges_FansOutToRegisteredCallbacks()
    {
        // CR-L402: exercise the PublishStateChanges fan-out lambda (resolves the registered
        // IEnumerable<Action<StateChangeRecord,string,Guid>> and invokes each) AND the engine's
        // onStateChanged invocation on a successful transition — previously only "engine resolves"
        // was asserted, so a regression in the callback wiring would have passed.
        StateChangeRecord? captured = null;
        string? capturedName = null;
        var capturedId = Guid.Empty;

        var services = new ServiceCollection();
        services.AddSingleton<Action<StateChangeRecord, string, Guid>>((record, name, id) =>
        {
            captured = record;
            capturedName = name;
            capturedId = id;
        });
        services.AddWorkflowEngine(options => options.PublishStateChanges = true);
        var provider = services.BuildServiceProvider();

        var engine = provider.GetRequiredService<IWorkflowEngine>();
        var workflow = new WorkflowBuilder<string>("DiFlow")
            .InitialState("A")
            .State("A").And()
            .State("B").IsFinal().And()
            .Transition("go", "A", "B").And()
            .Build();
        var instance = WorkflowInstance<string>.Create(workflow, "data");

        await engine.FireAsync(workflow, instance, "go");

        captured.Should().NotBeNull();
        captured!.FromState.Should().Be("A");
        captured.ToState.Should().Be("B");
        captured.Trigger.Should().Be("go");
        capturedName.Should().Be("DiFlow");
        capturedId.Should().Be(instance.InstanceId);
    }
}
