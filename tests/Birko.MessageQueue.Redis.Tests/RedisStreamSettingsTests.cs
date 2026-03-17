using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Redis.Tests
{
    public class RedisStreamSettingsTests
    {
        [Fact]
        public void DefaultConstructor_SetsDefaultValues()
        {
            var settings = new RedisStreamSettings();

            settings.Location.Should().Be("localhost");
            settings.Port.Should().Be(6379);
            settings.Database.Should().Be(0);
            settings.ConsumerGroup.Should().BeNull();
            settings.ConsumerName.Should().BeNull();
            settings.ReadCount.Should().Be(10);
            settings.BlockMilliseconds.Should().Be(5000);
            settings.MaxStreamLength.Should().BeNull();
            settings.AutoCreateConsumerGroup.Should().BeTrue();
            settings.StreamPrefix.Should().Be("birko:mq:stream");
        }

        [Fact]
        public void ParameterizedConstructor_SetsValues()
        {
            var settings = new RedisStreamSettings("redis.example.com", 6380, "secret", 2, true);

            settings.Location.Should().Be("redis.example.com");
            settings.Port.Should().Be(6380);
            settings.Password.Should().Be("secret");
            settings.Database.Should().Be(2);
            settings.UseSecure.Should().BeTrue();
        }

        [Fact]
        public void GetStreamKey_CombinesPrefixAndDestination()
        {
            var settings = new RedisStreamSettings
            {
                StreamPrefix = "myapp:streams"
            };

            settings.GetStreamKey("orders").Should().Be("myapp:streams:orders");
        }

        [Fact]
        public void GetStreamKey_UsesDefaultPrefix()
        {
            var settings = new RedisStreamSettings();

            settings.GetStreamKey("events").Should().Be("birko:mq:stream:events");
        }

        [Fact]
        public void ConsumerGroup_CanBeConfigured()
        {
            var settings = new RedisStreamSettings
            {
                ConsumerGroup = "order-service",
                ConsumerName = "worker-1"
            };

            settings.ConsumerGroup.Should().Be("order-service");
            settings.ConsumerName.Should().Be("worker-1");
        }

        [Fact]
        public void MaxStreamLength_CanBeConfigured()
        {
            var settings = new RedisStreamSettings
            {
                MaxStreamLength = 50000
            };

            settings.MaxStreamLength.Should().Be(50000);
        }

        [Fact]
        public void ReadCount_CanBeConfigured()
        {
            var settings = new RedisStreamSettings
            {
                ReadCount = 100
            };

            settings.ReadCount.Should().Be(100);
        }

        [Fact]
        public void BlockMilliseconds_CanBeNull()
        {
            var settings = new RedisStreamSettings
            {
                BlockMilliseconds = null
            };

            settings.BlockMilliseconds.Should().BeNull();
        }

        [Fact]
        public void InheritsRedisSettingsGetConnectionString()
        {
            var settings = new RedisStreamSettings("myhost", 6380, "pass123", 3);

            var connStr = settings.GetConnectionString();

            connStr.Should().Contain("myhost:6380");
            connStr.Should().Contain("password=pass123");
            connStr.Should().Contain("defaultDatabase=3");
        }

        [Fact]
        public void GetId_IncludesDatabaseIndex()
        {
            var settings = new RedisStreamSettings("host", 6379, database: 5);

            var id = settings.GetId();

            id.Should().Contain("5");
        }
    }
}
