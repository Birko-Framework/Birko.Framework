# Birko.MessageQueue.Redis.Tests

## Overview
Unit tests for Birko.MessageQueue.Redis — Redis Streams message queue implementation.

## Project Location
`tests/Birko.MessageQueue.Redis.Tests/` (.csproj test project)

## Components

### RedisStreamSettingsTests.cs
Tests for RedisStreamSettings configuration: defaults, constructors, stream key generation, connection string, GetId.

### RedisStreamQueueTests.cs
Tests for RedisStreamQueue: constructor validation, producer/consumer creation, dispose, connect/disconnect lifecycle.

### RedisProducerTests.cs
Tests for RedisProducer: destination validation (null, empty), dispose behavior.

### RedisConsumerTests.cs
Tests for RedisConsumer: destination validation, dispose behavior, ack/reject with unknown IDs.

### RedisSubscriptionTests.cs
Tests for RedisSubscription: subscribe/unsubscribe lifecycle, IsActive, double-unsubscribe, dispose.

## Dependencies
- Birko.MessageQueue (core interfaces)
- Birko.MessageQueue.Redis (implementation under test)
- Birko.Redis (RedisSettings, RedisConnectionManager)
- StackExchange.Redis
- xUnit 2.9.3, FluentAssertions 7.0.0

## Notes
- Tests that don't require a live Redis connection test validation, settings, and dispose behavior
- Integration tests requiring Redis are designed to fail gracefully when Redis is unavailable
