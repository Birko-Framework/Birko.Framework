using System;
using System.Text.Json;
using Birko.Serialization.Json;
using Birko.Serialization.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.Serialization.Tests.Json
{
    public class SystemJsonSerializerTests
    {
        private readonly SystemJsonSerializer _serializer = new();

        [Fact]
        public void ContentType_ReturnsApplicationJson()
        {
            _serializer.ContentType.Should().Be("application/json");
        }

        [Fact]
        public void Format_ReturnsJson()
        {
            _serializer.Format.Should().Be(SerializationFormat.Json);
        }

        [Fact]
        public void Serialize_Object_ReturnsJsonString()
        {
            var payload = new TestPayload { Name = "test", Value = 42, IsActive = true };
            var json = _serializer.Serialize((object)payload);

            json.Should().Contain("\"name\"");
            json.Should().Contain("\"test\"");
            json.Should().Contain("42");
        }

        [Fact]
        public void Serialize_Generic_ReturnsJsonString()
        {
            var payload = new TestPayload { Name = "test", Value = 42, IsActive = true };
            var json = _serializer.Serialize(payload);

            json.Should().Contain("\"name\"");
            json.Should().Contain("\"test\"");
        }

        [Fact]
        public void Deserialize_ByType_ReturnsObject()
        {
            var json = "{\"name\":\"test\",\"value\":42,\"isActive\":true}";
            var result = _serializer.Deserialize(json, typeof(TestPayload)) as TestPayload;

            result.Should().NotBeNull();
            result!.Name.Should().Be("test");
            result.Value.Should().Be(42);
            result.IsActive.Should().BeTrue();
        }

        [Fact]
        public void Deserialize_Generic_ReturnsTypedObject()
        {
            var json = "{\"name\":\"test\",\"value\":42,\"isActive\":true}";
            var result = _serializer.Deserialize<TestPayload>(json);

            result.Should().NotBeNull();
            result!.Name.Should().Be("test");
            result.Value.Should().Be(42);
        }

        [Fact]
        public void RoundTrip_String_PreservesData()
        {
            var original = new TestPayload { Name = "roundtrip", Value = 99, IsActive = false };
            var json = _serializer.Serialize(original);
            var result = _serializer.Deserialize<TestPayload>(json);

            result.Should().NotBeNull();
            result!.Name.Should().Be(original.Name);
            result.Value.Should().Be(original.Value);
            result.IsActive.Should().Be(original.IsActive);
        }

        [Fact]
        public void RoundTrip_Bytes_PreservesData()
        {
            var original = new TestPayload { Name = "bytes", Value = 7, IsActive = true };
            var bytes = _serializer.SerializeToBytes(original);
            var result = _serializer.DeserializeFromBytes<TestPayload>(bytes);

            result.Should().NotBeNull();
            result!.Name.Should().Be(original.Name);
            result.Value.Should().Be(original.Value);
            result.IsActive.Should().Be(original.IsActive);
        }

        [Fact]
        public void SerializeToBytes_Object_ReturnsUtf8Bytes()
        {
            var payload = new TestPayload { Name = "test", Value = 1, IsActive = true };
            var bytes = _serializer.SerializeToBytes((object)payload);

            bytes.Should().NotBeEmpty();
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            json.Should().Contain("\"name\"");
        }

        [Fact]
        public void DeserializeFromBytes_ByType_ReturnsObject()
        {
            var json = "{\"name\":\"test\",\"value\":42}";
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var result = _serializer.DeserializeFromBytes(bytes, typeof(TestPayload)) as TestPayload;

            result.Should().NotBeNull();
            result!.Name.Should().Be("test");
        }

        [Fact]
        public void Serialize_NullValue_ThrowsArgumentNullException()
        {
            var act = () => _serializer.Serialize((object)null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Deserialize_NullData_ThrowsArgumentNullException()
        {
            var act = () => _serializer.Deserialize<TestPayload>(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Serialize_NestedObject_PreservesStructure()
        {
            var original = new NestedPayload
            {
                Title = "parent",
                Tags = new List<string> { "a", "b" },
                Inner = new TestPayload { Name = "child", Value = 5, IsActive = true }
            };

            var json = _serializer.Serialize(original);
            var result = _serializer.Deserialize<NestedPayload>(json);

            result.Should().NotBeNull();
            result!.Title.Should().Be("parent");
            result.Tags.Should().BeEquivalentTo(new[] { "a", "b" });
            result.Inner.Should().NotBeNull();
            result.Inner!.Name.Should().Be("child");
        }

        [Fact]
        public void Constructor_CustomOptions_UsesProvidedOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                WriteIndented = true
            };
            var serializer = new SystemJsonSerializer(options);

            var payload = new TestPayload { Name = "test", Value = 1, IsActive = true };
            var json = serializer.Serialize(payload);

            json.Should().Contain("\"Name\"");
            json.Should().Contain("\n");
        }

        [Fact]
        public void Serialize_UsesCamelCaseByDefault()
        {
            var payload = new TestPayload { Name = "test", Value = 1, IsActive = true };
            var json = _serializer.Serialize(payload);

            json.Should().Contain("\"name\"");
            json.Should().Contain("\"value\"");
            json.Should().Contain("\"isActive\"");
            json.Should().NotContain("\"Name\"");
        }
    }
}
