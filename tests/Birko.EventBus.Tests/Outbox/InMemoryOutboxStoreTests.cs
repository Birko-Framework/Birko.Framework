using System;
using System.Threading.Tasks;
using Birko.EventBus.Outbox;
using Birko.EventBus.Outbox.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests.Outbox
{
    public class InMemoryOutboxStoreTests
    {
        [Fact]
        public async Task SaveAsync_StoresEntry()
        {
            var store = new InMemoryOutboxStore();
            var entry = CreateEntry();

            await store.SaveAsync(entry);

            store.GetAll().Should().ContainSingle().Which.EventId.Should().Be(entry.EventId);
        }

        [Fact]
        public async Task GetPendingAsync_ReturnsPendingOnly()
        {
            var store = new InMemoryOutboxStore();
            var pending = CreateEntry();
            var published = CreateEntry();
            published.Status = OutboxStatus.Published;

            await store.SaveAsync(pending);
            await store.SaveAsync(published);

            var result = await store.GetPendingAsync(10);
            result.Should().ContainSingle().Which.Id.Should().Be(pending.Id);
        }

        [Fact]
        public async Task GetPendingAsync_RespectsBatchSize()
        {
            var store = new InMemoryOutboxStore();
            for (int i = 0; i < 5; i++)
            {
                await store.SaveAsync(CreateEntry());
            }

            var result = await store.GetPendingAsync(3);
            result.Should().HaveCount(3);
        }

        [Fact]
        public async Task MarkPublishedAsync_SetsStatusAndTimestamp()
        {
            var store = new InMemoryOutboxStore();
            var entry = CreateEntry();
            await store.SaveAsync(entry);

            await store.MarkPublishedAsync(entry.Id);

            var all = store.GetAll();
            all.Should().ContainSingle()
                .Which.Status.Should().Be(OutboxStatus.Published);
            all[0].PublishedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task MarkFailedAsync_IncrementsAttempts()
        {
            var store = new InMemoryOutboxStore();
            var entry = CreateEntry();
            await store.SaveAsync(entry);

            await store.MarkFailedAsync(entry.Id, "Connection refused");

            var all = store.GetAll();
            all[0].Attempts.Should().Be(1);
            all[0].LastError.Should().Be("Connection refused");
            all[0].Status.Should().Be(OutboxStatus.Pending); // Still pending (under max attempts)
        }

        [Fact]
        public async Task MarkFailedAsync_SetsFailedAfterMaxAttempts()
        {
            var store = new InMemoryOutboxStore();
            var entry = CreateEntry();
            await store.SaveAsync(entry);

            // Fail 5 times (default max in InMemoryOutboxStore)
            for (int i = 0; i < 5; i++)
            {
                await store.MarkFailedAsync(entry.Id, $"Attempt {i + 1}");
            }

            store.GetAll()[0].Status.Should().Be(OutboxStatus.Failed);
        }

        [Fact]
        public async Task CleanupAsync_RemovesOldPublishedEntries()
        {
            var store = new InMemoryOutboxStore();
            var old = CreateEntry();
            old.Status = OutboxStatus.Published;
            old.CreatedAt = DateTime.UtcNow.AddDays(-10);
            await store.SaveAsync(old);

            var recent = CreateEntry();
            await store.SaveAsync(recent);

            await store.CleanupAsync(DateTime.UtcNow.AddDays(-7));

            store.GetAll().Should().ContainSingle().Which.Id.Should().Be(recent.Id);
        }

        private static OutboxEntry CreateEntry()
        {
            return new OutboxEntry
            {
                EventId = Guid.NewGuid(),
                EventType = "Test.OrderPlaced, TestAssembly",
                Payload = "{\"orderId\":\"123\"}",
                Source = "test"
            };
        }
    }
}
