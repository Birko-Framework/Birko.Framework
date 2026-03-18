# Birko.CQRS

Command Query Responsibility Segregation (CQRS) pattern implementation for the Birko Framework. Provides a simple mediator with typed commands, queries, handlers, and pipeline behaviors.

## Features

- **Commands & Queries** — Separate write (`ICommand`) and read (`IQuery<TResult>`) models
- **Mediator** — Dispatches requests to handlers resolved from DI
- **Pipeline Behaviors** — Russian-doll middleware for cross-cutting concerns (validation, logging, caching)
- **Type-safe** — Strongly-typed request/result pairs with compile-time checking
- **Zero dependencies** — Platform-agnostic shared project (only `Microsoft.Extensions.DependencyInjection.Abstractions`)

## Usage

### Define a Command

```csharp
// Command with no result
public record CreateOrderCommand(Guid CustomerId, IEnumerable<OrderItem> Items) : ICommand;

// Command with result
public record CreateOrderCommand(Guid CustomerId, IEnumerable<OrderItem> Items) : ICommand<Guid>;
```

### Define a Query

```csharp
public record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderViewModel?>;

public record GetOrdersQuery(Guid CustomerId, int Page, int PageSize) : IQuery<IEnumerable<OrderViewModel>>;
```

### Implement Handlers

```csharp
// Void command handler
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand>
{
    private readonly IAsyncRepository<Order> _orders;

    public CreateOrderHandler(IAsyncRepository<Order> orders)
    {
        _orders = orders;
    }

    public async Task<Unit> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = new Order { CustomerId = command.CustomerId };
        await _orders.CreateAsync(order, cancellationToken);
        return Unit.Value;
    }
}

// Query handler
public class GetOrderByIdHandler : IQueryHandler<GetOrderByIdQuery, OrderViewModel?>
{
    private readonly IAsyncBulkReadRepository<Order> _orders;

    public GetOrderByIdHandler(IAsyncBulkReadRepository<Order> orders)
    {
        _orders = orders;
    }

    public async Task<OrderViewModel?> HandleAsync(GetOrderByIdQuery query, CancellationToken cancellationToken = default)
    {
        var order = await _orders.ReadAsync(query.OrderId, cancellationToken);
        return order != null ? OrderViewModel.FromModel(order) : null;
    }
}
```

### Pipeline Behaviors

```csharp
// Logging behavior for all commands
public class LoggingBehavior<TCommand> : IPipelineBehavior<TCommand, Unit>
    where TCommand : ICommand
{
    private readonly ILogger _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TCommand>> logger)
    {
        _logger = logger;
    }

    public async Task<Unit> HandleAsync(TCommand request, Func<CancellationToken, Task<Unit>> next, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling {Command}", typeof(TCommand).Name);
        var result = await next(cancellationToken);
        _logger.LogInformation("Handled {Command}", typeof(TCommand).Name);
        return result;
    }
}
```

### DI Registration

```csharp
services.AddCqrs();
services.AddCommandHandler<CreateOrderCommand, CreateOrderHandler>();
services.AddQueryHandler<GetOrderByIdQuery, OrderViewModel?, GetOrderByIdHandler>();
services.AddPipelineBehavior<CreateOrderCommand, Unit, LoggingBehavior<CreateOrderCommand>>();
```

### Dispatching

```csharp
public class OrderController
{
    private readonly IMediator _mediator;

    public OrderController(IMediator mediator) => _mediator = mediator;

    public async Task CreateOrder(CreateOrderCommand command)
    {
        await _mediator.SendAsync(command);
    }

    public async Task<OrderViewModel?> GetOrder(Guid orderId)
    {
        return await _mediator.SendAsync(new GetOrderByIdQuery(orderId));
    }
}
```

## Integration with Birko.EventBus

Command handlers can publish domain events after completing write operations:

```csharp
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand>
{
    private readonly IAsyncRepository<Order> _orders;
    private readonly IEventBus _eventBus;

    public CreateOrderHandler(IAsyncRepository<Order> orders, IEventBus eventBus)
    {
        _orders = orders;
        _eventBus = eventBus;
    }

    public async Task<Unit> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = new Order { CustomerId = command.CustomerId };
        await _orders.CreateAsync(order, cancellationToken);
        await _eventBus.PublishAsync(new OrderCreatedEvent(order.Guid), cancellationToken);
        return Unit.Value;
    }
}
```

## License

MIT License - see [License.md](License.md) for details.
