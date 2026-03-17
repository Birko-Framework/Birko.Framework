using Birko.Workflow.Core;
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
}
