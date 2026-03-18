using Birko.CQRS;
using Birko.CQRS.Extensions;
using Birko.CQRS.Pipeline;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.CQRS.Tests;

public class DiRegistrationTests
{
    [Fact]
    public void AddCqrs_RegistersMediatorAsScoped()
    {
        var services = new ServiceCollection();
        services.AddCqrs();
        using var provider = services.BuildServiceProvider();

        var mediator1 = provider.GetService<IMediator>();
        mediator1.Should().NotBeNull();
        mediator1.Should().BeOfType<Mediator>();
    }

    [Fact]
    public void AddCqrs_ScopedMediator_DifferentPerScope()
    {
        var services = new ServiceCollection();
        services.AddCqrs();
        using var provider = services.BuildServiceProvider();

        IMediator mediator1, mediator2;
        using (var scope1 = provider.CreateScope())
        {
            mediator1 = scope1.ServiceProvider.GetRequiredService<IMediator>();
        }
        using (var scope2 = provider.CreateScope())
        {
            mediator2 = scope2.ServiceProvider.GetRequiredService<IMediator>();
        }

        mediator1.Should().NotBeSameAs(mediator2);
    }

    [Fact]
    public void AddCommandHandler_RegistersAsTransient()
    {
        var services = new ServiceCollection();
        services.AddCommandHandler<CreateItemCommand, CreateItemHandler>();
        using var provider = services.BuildServiceProvider();

        var handler = provider.GetService<IRequestHandler<CreateItemCommand, Unit>>();
        handler.Should().NotBeNull();
        handler.Should().BeOfType<CreateItemHandler>();
    }

    [Fact]
    public void AddCommandHandler_WithResult_RegistersAsTransient()
    {
        var services = new ServiceCollection();
        services.AddCommandHandler<CreateItemWithIdCommand, Guid, CreateItemWithIdHandler>();
        using var provider = services.BuildServiceProvider();

        var handler = provider.GetService<IRequestHandler<CreateItemWithIdCommand, Guid>>();
        handler.Should().NotBeNull();
        handler.Should().BeOfType<CreateItemWithIdHandler>();
    }

    [Fact]
    public void AddQueryHandler_RegistersAsTransient()
    {
        var services = new ServiceCollection();
        services.AddQueryHandler<GetItemQuery, string?, GetItemHandler>();
        using var provider = services.BuildServiceProvider();

        var handler = provider.GetService<IRequestHandler<GetItemQuery, string?>>();
        handler.Should().NotBeNull();
        handler.Should().BeOfType<GetItemHandler>();
    }

    [Fact]
    public void AddPipelineBehavior_RegistersAsTransient()
    {
        var services = new ServiceCollection();
        services.AddPipelineBehavior<CreateItemCommand, Unit, OuterBehavior>();
        using var provider = services.BuildServiceProvider();

        var behaviors = provider.GetServices<IPipelineBehavior<CreateItemCommand, Unit>>();
        behaviors.Should().ContainSingle().Which.Should().BeOfType<OuterBehavior>();
    }

    [Fact]
    public void AddCqrs_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();
        var result = services.AddCqrs();
        result.Should().BeSameAs(services);
    }
}
