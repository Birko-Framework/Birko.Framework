using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Tests.Serialization
{
    public class TestPayload
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class JsonMessageSerializerTests
    {
        private readonly JsonMessageSerializer _serializer = new();

        [Fact]
        public void ContentType_IsJson()
        {
            _serializer.ContentType.Should().Be("application/json");
        }

        [Fact]
        public void Serialize_ProducesJson()
        {
            var json = _serializer.Serialize(new TestPayload { Name = "test", Value = 42 });

            json.Should().Contain("\"name\"");
            json.Should().Contain("\"value\"");
            json.Should().Contain("42");
        }

        [Fact]
        public void Deserialize_Typed_ReturnsObject()
        {
            var json = _serializer.Serialize(new TestPayload { Name = "hello", Value = 99 });
            var result = _serializer.Deserialize<TestPayload>(json);

            result.Should().NotBeNull();
            result!.Name.Should().Be("hello");
            result.Value.Should().Be(99);
        }

        [Fact]
        public void Deserialize_ByType_ReturnsObject()
        {
            var json = _serializer.Serialize(new TestPayload { Name = "world", Value = 7 });
            var result = _serializer.Deserialize(json, typeof(TestPayload)) as TestPayload;

            result.Should().NotBeNull();
            result!.Name.Should().Be("world");
            result.Value.Should().Be(7);
        }

        [Fact]
        public void RoundTrip_PreservesData()
        {
            var original = new TestPayload { Name = "roundtrip", Value = 123 };
            var json = _serializer.Serialize(original);
            var restored = _serializer.Deserialize<TestPayload>(json);

            restored.Should().NotBeNull();
            restored!.Name.Should().Be(original.Name);
            restored.Value.Should().Be(original.Value);
        }
    }
}
