# Birko.CQRS

## Overview
Command Query Responsibility Segregation (CQRS) pattern implementation with mediator, pipeline behaviors, and DI integration.

## Project Location
`C:\Source\Birko.CQRS\` (shared project: `.shproj` + `.projitems`)

## Components

### Core (`Core/`)
- **IRequest<TResult>** — Base marker interface for all requests (commands and queries)
- **ICommand** — Command with no result (extends `IRequest<Unit>`)
- **ICommand<TResult>** — Command with a result (extends `IRequest<TResult>`)
- **IQuery<TResult>** — Query returning a result (extends `IRequest<TResult>`)
- **IRequestHandler<TRequest, TResult>** — Base handler interface used by the mediator
- **ICommandHandler<TCommand>** — Handler for void commands (extends `IRequestHandler<TCommand, Unit>`)
- **ICommandHandler<TCommand, TResult>** — Handler for commands with results
- **IQueryHandler<TQuery, TResult>** — Handler for queries
- **Unit** — Void return type struct for commands that produce no result

### Pipeline (`Pipeline/`)
- **IPipelineBehavior<TRequest, TResult>** — Middleware behavior in the request pipeline (Russian-doll pattern)
- **RequestPipeline<TRequest, TResult>** — Executes ordered chain of behaviors around a handler

### Mediator (`Mediator/`)
- **IMediator** — Dispatches requests to handlers through the pipeline
- **Mediator** — Default implementation resolving handlers and behaviors from DI

### Extensions (`Extensions/`)
- **CqrsServiceCollectionExtensions** — DI registration: `AddCqrs()`, `AddCommandHandler<>()`, `AddQueryHandler<>()`, `AddPipelineBehavior<>()`

## Type Hierarchy
```
IRequest<TResult>
├── ICommand : IRequest<Unit>
├── ICommand<TResult> : IRequest<TResult>
└── IQuery<TResult> : IRequest<TResult>

IRequestHandler<TRequest, TResult>
├── ICommandHandler<TCommand> : IRequestHandler<TCommand, Unit>
├── ICommandHandler<TCommand, TResult> : IRequestHandler<TCommand, TResult>
└── IQueryHandler<TQuery, TResult> : IRequestHandler<TQuery, TResult>
```

## Dependencies
- `Microsoft.Extensions.DependencyInjection.Abstractions` (DI extensions)

## Integration Points
- **Birko.EventBus** — Command handlers can publish domain events via `IEventBus.PublishAsync<TEvent>`
- **Birko.Validation** — Pipeline behaviors can validate commands before execution
- **Birko.Telemetry** — Pipeline behaviors can trace request execution

## Maintenance
When modifying this project:
- Update this CLAUDE.md with any new interfaces or components
- Update README.md with usage examples
- Update `docs/cqrs.md` if documentation exists
- Ensure all new files are listed in `Birko.CQRS.projitems`
