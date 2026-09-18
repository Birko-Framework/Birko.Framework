using Birko.CQRS;
using Birko.CQRS.Extensions;
using Birko.CQRS.Pipeline;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.CQRS.Tests;

#region Test Commands and Queries

public record CreateItemCommand(string Name) : ICommand;

public record CreateItemWithIdCommand(string Name) : ICommand<Guid>;

public record GetItemQuery(Guid Id) : IQuery<string?>;

public record UnhandledCommand : ICommand;

#endregion

#region Test Handlers

public class CreateItemHandler : ICommandHandler<CreateItemCommand>
{
    public static bool WasHandled { get; set; }

    public Task<Unit> HandleAsync(CreateItemCommand request, CancellationToken cancellationToken = default)
    {
        WasHandled = true;
        return Unit.Task;
    }
}

public class CreateItemWithIdHandler : ICommandHandler<CreateItemWithIdCommand, Guid>
{
    public static Guid LastCreatedId { get; set; }

    public Task<Guid> HandleAsync(CreateItemWithIdCommand request, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        LastCreatedId = id;
        return Task.FromResult(id);
    }
}

public class GetItemHandler : IQueryHandler<GetItemQuery, string?>
{
    public Task<string?> HandleAsync(GetItemQuery request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(request.Id == Guid.Empty ? null : $"Item-{request.Id}");
    }
}

#endregion

public class MediatorTests
{
    private ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddCqrs();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SendAsync_VoidCommand_InvokesHandler()
    {
        CreateItemHandler.WasHandled = false;
        using var provider = BuildProvider(s =>
            s.AddCommandHandler<CreateItemCommand, CreateItemHandler>());
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.SendAsync(new CreateItemCommand("Test"));

        CreateItemHandler.WasHandled.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_CommandWithResult_ReturnsResult()
    {
        using var provider = BuildProvider(s =>
            s.AddCommandHandler<CreateItemWithIdCommand, Guid, CreateItemWithIdHandler>());
        var mediator = provider.GetRequiredService<IMediator>();

        var id = await mediator.SendAsync<Guid>(new CreateItemWithIdCommand("Test"));

        id.Should().NotBe(Guid.Empty);
        id.Should().Be(CreateItemWithIdHandler.LastCreatedId);
    }

    [Fact]
    public async Task SendAsync_Query_ReturnsResult()
    {
        using var provider = BuildProvider(s =>
            s.AddQueryHandler<GetItemQuery, string?, GetItemHandler>());
        var mediator = provider.GetRequiredService<IMediator>();

        var itemId = Guid.NewGuid();
        var result = await mediator.SendAsync<string?>(new GetItemQuery(itemId));

        result.Should().Be($"Item-{itemId}");
    }

    [Fact]
    public async Task SendAsync_CovariantDispatch_DoesNotPoisonHandlerCache()
    {
        // Regression for CR-H039: the handler cache was keyed on request type only, ignoring
        // TResult. IRequest<out TResult> is covariant, so an IQuery<string?> can be dispatched as
        // SendAsync<object?>. That used to cache a wrapper<GetItemQuery, object?>; a later correct
        // SendAsync<string?> then reused it and threw InvalidCastException. The cache is now keyed
        // on (requestType, resultType).
        using var provider = BuildProvider(s =>
            s.AddQueryHandler<GetItemQuery, string?, GetItemHandler>());
        var mediator = provider.GetRequiredService<IMediator>();
        var query = new GetItemQuery(Guid.NewGuid());

        // Covariant dispatch with a different TResult (no handler registered for object?).
        var covariant = async () => await mediator.SendAsync<object?>(query);
        await covariant.Should().ThrowAsync<InvalidOperationException>("no IRequestHandler<GetItemQuery, object?> is registered");

        // The correct dispatch must still succeed — the covariant call must not have poisoned the cache.
        var result = await mediator.SendAsync<string?>(query);
        result.Should().Be($"Item-{query.Id}");
    }

    [Fact]
    public async Task SendAsync_Query_ReturnsNull()
    {
        using var provider = BuildProvider(s =>
            s.AddQueryHandler<GetItemQuery, string?, GetItemHandler>());
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.SendAsync<string?>(new GetItemQuery(Guid.Empty));

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_NoHandler_ThrowsInvalidOperation()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var act = () => mediator.SendAsync(new UnhandledCommand());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No handler registered*UnhandledCommand*");
    }

    [Fact]
    public async Task SendAsync_NullRequest_ThrowsArgumentNull()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var act = () => mediator.SendAsync<Unit>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullServiceProvider_ThrowsArgumentNull()
    {
        var act = () => new Mediator(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
