# Birko.CQRS.Tests

## Overview
Unit tests for Birko.CQRS — mediator, pipeline behaviors, DI registration, and core types.

## Project Location
`C:\Source\Birko.CQRS.Tests\` (.csproj, xUnit + FluentAssertions)

## Components
- **UnitTests.cs** — Unit struct tests (equality, comparison, hash, toString, task)
- **MediatorTests.cs** — Mediator dispatch tests (void commands, commands with results, queries, error cases)
- **PipelineTests.cs** — Pipeline behavior tests (ordering, short-circuit, no-behaviors, query behaviors)
- **DiRegistrationTests.cs** — DI registration tests (AddCqrs, AddCommandHandler, AddQueryHandler, AddPipelineBehavior)

## Test Fixtures (shared across files)
- **CreateItemCommand** — Void command
- **CreateItemWithIdCommand** — Command returning Guid
- **GetItemQuery** — Query returning string?
- **CreateItemHandler** — Void command handler with static WasHandled flag
- **CreateItemWithIdHandler** — Command handler returning Guid
- **GetItemHandler** — Query handler
- **OuterBehavior / InnerBehavior** — Pipeline behaviors with execution logging
- **ShortCircuitBehavior** — Pipeline behavior that skips handler
- **QueryLoggingBehavior** — Query-specific pipeline behavior

## Dependencies
- Birko.CQRS (shared project import)
- Microsoft.Extensions.DependencyInjection 10.0.0-preview
- xUnit 2.9.3, FluentAssertions 7.0.0
