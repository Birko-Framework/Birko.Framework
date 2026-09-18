using System;
using FluentAssertions;
using Xunit;
using Birko.BackgroundJobs.Serialization;

namespace Birko.BackgroundJobs.Tests.Serialization
{
    public class JsonJobSerializerTests
    {
        private readonly JsonJobSerializer _serializer = new();

        [Fact]
        public void RoundTrip_SimpleObject()
        {
            var input = new EmailInput { To = "test@example.com", Subject = "Hello", Body = "World" };

            var json = _serializer.Serialize(input);
            var result = _serializer.Deserialize(json, typeof(EmailInput)) as EmailInput;

            result.Should().NotBeNull();
            result!.To.Should().Be("test@example.com");
            result.Subject.Should().Be("Hello");
            result.Body.Should().Be("World");
        }

        [Fact]
        public void Serialize_ProducesCamelCase()
        {
            var input = new EmailInput { To = "a@b.com", Subject = "Test", Body = "Content" };

            var json = _serializer.Serialize(input);

            json.Should().Contain("\"to\":");
            json.Should().Contain("\"subject\":");
            json.Should().Contain("\"body\":");
        }

        [Fact]
        public void Deserialize_InvalidType_ThrowsJsonException()
        {
            var json = "\"just a string\"";

            var act = () => _serializer.Deserialize(json, typeof(EmailInput));

            act.Should().Throw<System.Text.Json.JsonException>();
        }

        [Fact]
        public void RoundTrip_AnonymousLikeObject()
        {
            var input = new TestPayload { Count = 42, Name = "test", Active = true };

            var json = _serializer.Serialize(input);
            var result = _serializer.Deserialize(json, typeof(TestPayload)) as TestPayload;

            result.Should().NotBeNull();
            result!.Count.Should().Be(42);
            result.Name.Should().Be("test");
            result.Active.Should().BeTrue();
        }

        private class TestPayload
        {
            public int Count { get; set; }
            public string Name { get; set; } = string.Empty;
            public bool Active { get; set; }
        }
    }
}
