using System;
using Birko.Serialization.Newtonsoft;
using Birko.Serialization.Tests.TestResources;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace Birko.Serialization.Tests.Newtonsoft
{
    public class NewtonsoftJsonSerializerTests
    {
        private readonly NewtonsoftJsonSerializer _serializer = new();

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
        }

        [Fact]
        public void Serialize_UsesCamelCaseByDefault()
        {
            var payload = new TestPayload { Name = "test", Value = 1, IsActive = true };
            var json = _serializer.Serialize(payload);

            json.Should().Contain("\"name\"");
            json.Should().Contain("\"value\"");
            json.Should().Contain("\"isActive\"");
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
        public void Deserialize_ByType_ReturnsObject()
        {
            var json = "{\"name\":\"test\",\"value\":42}";
            var result = _serializer.Deserialize(json, typeof(TestPayload)) as TestPayload;

            result.Should().NotBeNull();
            result!.Name.Should().Be("test");
            result.Value.Should().Be(42);
        }

        [Fact]
        public void Constructor_CustomSettings_UsesProvidedSettings()
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
            var serializer = new NewtonsoftJsonSerializer(settings);

            var payload = new TestPayload { Name = "test", Value = 1, IsActive = true };
            var json = serializer.Serialize(payload);

            json.Should().Contain("\n");
        }

        [Fact]
        public void Serialize_NestedObject_PreservesStructure()
        {
            var original = new NestedPayload
            {
                Title = "parent",
                Tags = new List<string> { "x", "y" },
                Inner = new TestPayload { Name = "child", Value = 3, IsActive = false }
            };

            var json = _serializer.Serialize(original);
            var result = _serializer.Deserialize<NestedPayload>(json);

            result.Should().NotBeNull();
            result!.Title.Should().Be("parent");
            result.Tags.Should().BeEquivalentTo(new[] { "x", "y" });
            result.Inner.Should().NotBeNull();
            result.Inner!.Name.Should().Be("child");
        }
    }
}
