using Birko.CQRS;
using Birko.CQRS.Extensions;
using Birko.CQRS.Pipeline;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.CQRS.Tests;

#region Test Pipeline Behaviors

public class OuterBehavior : IPipelineBehavior<CreateItemCommand, Unit>
{
    public static List<string> ExecutionLog { get; } = [];

    public async Task<Unit> HandleAsync(CreateItemCommand request, Func<CancellationToken, Task<Unit>> next, CancellationToken cancellationToken = default)
    {
        ExecutionLog.Add("Outer-Before");
        var result = await next(cancellationToken);
        ExecutionLog.Add("Outer-After");
        return result;
    }
}

public class InnerBehavior : IPipelineBehavior<CreateItemCommand, Unit>
{
    public async Task<Unit> HandleAsync(CreateItemCommand request, Func<CancellationToken, Task<Unit>> next, CancellationToken cancellationToken = default)
    {
        OuterBehavior.ExecutionLog.Add("Inner-Before");
        var result = await next(cancellationToken);
        OuterBehavior.ExecutionLog.Add("Inner-After");
        return result;
    }
}

public class ShortCircuitBehavior : IPipelineBehavior<CreateItemCommand, Unit>
{
    public Task<Unit> HandleAsync(CreateItemCommand request, Func<CancellationToken, Task<Unit>> next, CancellationToken cancellationToken = default)
    {
        OuterBehavior.ExecutionLog.Add("ShortCircuit");
        return Unit.Task; // does not call next
    }
}

public class QueryLoggingBehavior : IPipelineBehavior<GetItemQuery, string?>
{
    public static bool WasExecuted { get; set; }

    public async Task<string?> HandleAsync(GetItemQuery request, Func<CancellationToken, Task<string?>> next, CancellationToken cancellationToken = default)
    {
        WasExecuted = true;
        return await next(cancellationToken);
    }
}

#endregion

public class PipelineTests
{
    private ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddCqrs();
        services.AddCommandHandler<CreateItemCommand, CreateItemHandler>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Pipeline_ExecutesBehaviorsInOrder()
    {
        CreateItemHandler.WasHandled = false;
        OuterBehavior.ExecutionLog.Clear();

        using var provider = BuildProvider(s =>
        {
            s.AddPipelineBehavior<CreateItemCommand, Unit, OuterBehavior>();
            s.AddPipelineBehavior<CreateItemCommand, Unit, InnerBehavior>();
        });
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.SendAsync(new CreateItemCommand("Test"));

        OuterBehavior.ExecutionLog.Should().Equal(
            "Outer-Before", "Inner-Before", "Inner-After", "Outer-After");
        CreateItemHandler.WasHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_ShortCircuit_DoesNotCallHandler()
    {
        CreateItemHandler.WasHandled = false;
        OuterBehavior.ExecutionLog.Clear();

        using var provider = BuildProvider(s =>
            s.AddPipelineBehavior<CreateItemCommand, Unit, ShortCircuitBehavior>());
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.SendAsync(new CreateItemCommand("Test"));

        OuterBehavior.ExecutionLog.Should().Equal("ShortCircuit");
        CreateItemHandler.WasHandled.Should().BeFalse();
    }

    [Fact]
    public async Task Pipeline_NoBehaviors_CallsHandlerDirectly()
    {
        CreateItemHandler.WasHandled = false;

        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.SendAsync(new CreateItemCommand("Test"));

        CreateItemHandler.WasHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Pipeline_QueryBehavior_IsExecuted()
    {
        QueryLoggingBehavior.WasExecuted = false;

        var services = new ServiceCollection();
        services.AddCqrs();
        services.AddQueryHandler<GetItemQuery, string?, GetItemHandler>();
        services.AddPipelineBehavior<GetItemQuery, string?, QueryLoggingBehavior>();
        using var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var result = await mediator.SendAsync<string?>(new GetItemQuery(Guid.NewGuid()));

        QueryLoggingBehavior.WasExecuted.Should().BeTrue();
        result.Should().NotBeNull();
    }

    [Fact]
    public void RequestPipeline_NullBehaviors_HandlesGracefully()
    {
        var pipeline = new RequestPipeline<CreateItemCommand, Unit>(null!);
        // should not throw, just call handler directly
        var called = false;
        var task = pipeline.ExecuteAsync(new CreateItemCommand("Test"), ct =>
        {
            called = true;
            return Unit.Task;
        });
        task.IsCompleted.Should().BeTrue();
        called.Should().BeTrue();
    }
}
