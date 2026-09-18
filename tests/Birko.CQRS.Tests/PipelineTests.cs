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

// CR-L099: a behavior that throws, to assert exception propagation through the chain.
public class ThrowingBehavior : IPipelineBehavior<CreateItemCommand, Unit>
{
    public Task<Unit> HandleAsync(CreateItemCommand request, Func<CancellationToken, Task<Unit>> next, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("boom from behavior");
}

// CR-L099: a behavior that observes the token, to assert cancellation surfaces through SendAsync.
public class TokenObservingBehavior : IPipelineBehavior<CreateItemCommand, Unit>
{
    public Task<Unit> HandleAsync(CreateItemCommand request, Func<CancellationToken, Task<Unit>> next, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return next(cancellationToken);
    }
}

#endregion

[Collection(CreateItemHandlerStateCollection.Name)]
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
    public async Task Pipeline_ExceptionInBehavior_PropagatesAndSkipsOuterAfter()
    {
        // CR-L099: an exception thrown in an inner behavior propagates out of SendAsync, and the outer
        // behavior's post-next code does not run (the throw unwinds the chain).
        CreateItemHandler.WasHandled = false;
        OuterBehavior.ExecutionLog.Clear();

        using var provider = BuildProvider(s =>
        {
            s.AddPipelineBehavior<CreateItemCommand, Unit, OuterBehavior>();
            s.AddPipelineBehavior<CreateItemCommand, Unit, ThrowingBehavior>();
        });
        var mediator = provider.GetRequiredService<IMediator>();

        var act = async () => await mediator.SendAsync(new CreateItemCommand("Test"));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("boom from behavior");
        CreateItemHandler.WasHandled.Should().BeFalse();
        OuterBehavior.ExecutionLog.Should().Equal("Outer-Before"); // no "Outer-After"
    }

    [Fact]
    public async Task Pipeline_PreCancelledToken_SurfacesOperationCanceled()
    {
        // CR-L099: a pre-cancelled token flows through the pipeline and surfaces as OperationCanceledException.
        using var provider = BuildProvider(s =>
            s.AddPipelineBehavior<CreateItemCommand, Unit, TokenObservingBehavior>());
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await mediator.SendAsync(new CreateItemCommand("Test"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
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
