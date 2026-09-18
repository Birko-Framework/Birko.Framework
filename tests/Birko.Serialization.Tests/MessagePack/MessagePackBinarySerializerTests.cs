using System;
using Birko.Serialization.MessagePack;
using Birko.Serialization.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.Serialization.Tests.MessagePack
{
    public class MessagePackBinarySerializerTests
    {
        private readonly MessagePackBinarySerializer _serializer = new();

        [Fact]
        public void ContentType_ReturnsMsgpack()
        {
            _serializer.ContentType.Should().Be("application/x-msgpack");
        }

        [Fact]
        public void Format_ReturnsMessagePack()
        {
            _serializer.Format.Should().Be(SerializationFormat.MessagePack);
        }

        [Fact]
        public void RoundTrip_Bytes_PreservesData()
        {
            var original = new TestPayload { Name = "msgpack", Value = 55, IsActive = true };
            var bytes = _serializer.SerializeToBytes(original);
            var result = _serializer.DeserializeFromBytes<TestPayload>(bytes);

            result.Should().NotBeNull();
            result!.Name.Should().Be(original.Name);
            result.Value.Should().Be(original.Value);
            result.IsActive.Should().Be(original.IsActive);
        }

        [Fact]
        public void RoundTrip_String_PreservesData()
        {
            var original = new TestPayload { Name = "base64", Value = 10, IsActive = false };
            var encoded = _serializer.Serialize(original);
            var result = _serializer.Deserialize<TestPayload>(encoded);

            result.Should().NotBeNull();
            result!.Name.Should().Be(original.Name);
            result.Value.Should().Be(original.Value);
        }

        [Fact]
        public void Serialize_String_ReturnsBase64()
        {
            var payload = new TestPayload { Name = "test", Value = 1, IsActive = true };
            var encoded = _serializer.Serialize(payload);

            var act = () => Convert.FromBase64String(encoded);
            act.Should().NotThrow();
        }

        [Fact]
        public void SerializeToBytes_ProducesCompactBinary()
        {
            var payload = new TestPayload { Name = "test", Value = 1, IsActive = true };
            var bytes = _serializer.SerializeToBytes(payload);

            bytes.Should().NotBeEmpty();
            var jsonSerializer = new Birko.Serialization.Json.SystemJsonSerializer();
            var jsonBytes = jsonSerializer.SerializeToBytes(payload);
            bytes.Length.Should().BeLessThan(jsonBytes.Length);
        }

        [Fact]
        public void Serialize_NullValue_ThrowsArgumentNullException()
        {
            var act = () => _serializer.Serialize((object)null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void DeserializeFromBytes_NullData_ThrowsArgumentNullException()
        {
            var act = () => _serializer.DeserializeFromBytes<TestPayload>(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void RoundTrip_Bytes_ByType_PreservesData()
        {
            var original = new TestPayload { Name = "typed", Value = 77, IsActive = true };
            var bytes = _serializer.SerializeToBytes((object)original);
            var result = _serializer.DeserializeFromBytes(bytes, typeof(TestPayload)) as TestPayload;

            result.Should().NotBeNull();
            result!.Name.Should().Be(original.Name);
            result.Value.Should().Be(original.Value);
        }

        [Fact]
        public void RoundTrip_NestedObject_PreservesStructure()
        {
            var original = new NestedPayload
            {
                Title = "parent",
                Tags = new List<string> { "a", "b" },
                Inner = new TestPayload { Name = "child", Value = 5, IsActive = true }
            };

            var bytes = _serializer.SerializeToBytes(original);
            var result = _serializer.DeserializeFromBytes<NestedPayload>(bytes);

            result.Should().NotBeNull();
            result!.Title.Should().Be("parent");
            result.Tags.Should().BeEquivalentTo(new[] { "a", "b" });
            result.Inner.Should().NotBeNull();
            result.Inner!.Name.Should().Be("child");
        }
    }
}
