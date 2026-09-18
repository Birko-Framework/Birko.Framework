# Birko.CQRS — Command Query Responsibility Segregation

## Overview

Birko.CQRS provides a lightweight CQRS implementation with a mediator pattern, typed commands/queries, handler resolution via DI, and a pipeline behavior middleware system.

## Core Concepts

### Commands vs Queries

| Aspect | Command | Query |
|--------|---------|-------|
| Purpose | Write/mutate state | Read state |
| Interface | `ICommand` / `ICommand<TResult>` | `IQuery<TResult>` |
| Return | `Unit` (void) or typed result | Always typed result |
| Side effects | Yes | No |

### Type Hierarchy

```
IRequest<TResult>                          (base marker)
├── ICommand : IRequest<Unit>              (void command)
├── ICommand<TResult> : IRequest<TResult>  (command with result)
└── IQuery<TResult> : IRequest<TResult>    (query)

IRequestHandler<TRequest, TResult>         (base handler)
├── ICommandHandler<TCommand>              (void command handler)
├── ICommandHandler<TCommand, TResult>     (result command handler)
└── IQueryHandler<TQuery, TResult>         (query handler)
```

## Usage

### Define Commands

```csharp
// Void command
public record CreateOrderCommand(Guid CustomerId, List<OrderItem> Items) : ICommand;

// Command with result
public record PlaceOrderCommand(Guid CustomerId, List<OrderItem> Items) : ICommand<Guid>;
```

### Define Queries

```csharp
public record GetOrderQuery(Guid OrderId) : IQuery<OrderViewModel?>;
public record ListOrdersQuery(Guid CustomerId, int Page, int PageSize) : IQuery<IEnumerable<OrderViewModel>>;
```

### Implement Handlers

```csharp
public class CreateOrderHandler : ICommandHandler<CreateOrderCommand>
{
    private readonly IAsyncRepository<Order> _orders;

    public CreateOrderHandler(IAsyncRepository<Order> orders) => _orders = orders;

    public async Task<Unit> HandleAsync(CreateOrderCommand command, CancellationToken ct = default)
    {
        var order = new Order { CustomerId = command.CustomerId };
        await _orders.CreateAsync(order, ct);
        return Unit.Value;
    }
}

public class GetOrderHandler : IQueryHandler<GetOrderQuery, OrderViewModel?>
{
    private readonly IAsyncReadRepository<Order> _orders;

    public GetOrderHandler(IAsyncReadRepository<Order> orders) => _orders = orders;

    public async Task<OrderViewModel?> HandleAsync(GetOrderQuery query, CancellationToken ct = default)
    {
        var order = await _orders.ReadAsync(query.OrderId, ct);
        return order != null ? OrderViewModel.From(order) : null;
    }
}
```

### Pipeline Behaviors

Pipeline behaviors wrap handler execution in a Russian-doll pattern (outermost registered first):

```csharp
public class ValidationBehavior<TCommand> : IPipelineBehavior<TCommand, Unit>
    where TCommand : ICommand
{
    private readonly IValidator<TCommand> _validator;

    public ValidationBehavior(IValidator<TCommand> validator) => _validator = validator;

    public async Task<Unit> HandleAsync(TCommand request, Func<CancellationToken, Task<Unit>> next, CancellationToken ct = default)
    {
        var result = _validator.Validate(request);
        if (!result.IsValid)
            throw new ValidationException(result.Errors);
        return await next(ct);
    }
}
```

### DI Registration

```csharp
services.AddCqrs();  // registers IMediator as scoped

// Commands
services.AddCommandHandler<CreateOrderCommand, CreateOrderHandler>();
services.AddCommandHandler<PlaceOrderCommand, Guid, PlaceOrderHandler>();

// Queries
services.AddQueryHandler<GetOrderQuery, OrderViewModel?, GetOrderHandler>();

// Pipeline behaviors
services.AddPipelineBehavior<CreateOrderCommand, Unit, ValidationBehavior<CreateOrderCommand>>();
```

### Dispatching

```csharp
public class OrderEndpoints
{
    public static async Task<IResult> CreateOrder(IMediator mediator, CreateOrderCommand command)
    {
        await mediator.SendAsync(command);
        return Results.Ok();
    }

    public static async Task<IResult> GetOrder(IMediator mediator, Guid id)
    {
        var order = await mediator.SendAsync(new GetOrderQuery(id));
        return order != null ? Results.Ok(order) : Results.NotFound();
    }
}
```

## Integration with EventBus

Commands can publish domain events after write operations:

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

    public async Task<Unit> HandleAsync(CreateOrderCommand command, CancellationToken ct = default)
    {
        var order = new Order { CustomerId = command.CustomerId };
        await _orders.CreateAsync(order, ct);
        await _eventBus.PublishAsync(new OrderCreatedEvent(order.Guid), ct);
        return Unit.Value;
    }
}
```

## Integration with Validation

Use `Birko.Validation` validators inside pipeline behaviors for automatic command validation before handler execution.

## Architecture

```
Controller/Endpoint
    └── IMediator.SendAsync(request)
            └── Pipeline Behavior 1 (outermost)
                └── Pipeline Behavior 2
                    └── IRequestHandler.HandleAsync(request)
                            ├── Repository/Store operations
                            └── IEventBus.PublishAsync (optional)
```

## Dependencies

- `Microsoft.Extensions.DependencyInjection.Abstractions`
