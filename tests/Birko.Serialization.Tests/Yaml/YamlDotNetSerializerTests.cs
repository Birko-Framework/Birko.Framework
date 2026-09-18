using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Birko.Serialization.Tests.TestResources;
using Birko.Serialization.Yaml;
using FluentAssertions;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Birko.Serialization.Tests.Yaml
{
    public class YamlDotNetSerializerTests
    {
        private readonly YamlDotNetSerializer _serializer = new();

        [Fact]
        public void ContentType_ReturnsApplicationYaml()
        {
            _serializer.ContentType.Should().Be("application/yaml");
        }

        [Fact]
        public void Format_ReturnsYaml()
        {
            _serializer.Format.Should().Be(SerializationFormat.Yaml);
        }

        [Fact]
        public void RoundTrip_String_PreservesData()
        {
            var original = new TestPayload { Name = "roundtrip", Value = 99, IsActive = false };
            var yaml = _serializer.Serialize(original);
            var result = _serializer.Deserialize<TestPayload>(yaml);

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
        public async Task RoundTrip_Stream_PreservesData()
        {
            var original = new TestPayload { Name = "stream", Value = 42, IsActive = true };

            using var stream = new MemoryStream();
            await _serializer.SerializeAsync(stream, original);
            stream.Position = 0;
            var result = await _serializer.DeserializeAsync<TestPayload>(stream);

            result.Should().NotBeNull();
            result!.Name.Should().Be(original.Name);
            result.Value.Should().Be(original.Value);
            result.IsActive.Should().Be(original.IsActive);
        }

        [Fact]
        public void Serialize_UsesCamelCaseByDefault()
        {
            var payload = new TestPayload { Name = "test", Value = 1, IsActive = true };
            var yaml = _serializer.Serialize(payload);

            yaml.Should().Contain("name:");
            yaml.Should().Contain("value:");
            yaml.Should().Contain("isActive:");
        }

        [Fact]
        public void SerializeToBytes_ProducesUtf8()
        {
            var payload = new TestPayload { Name = "utf8", Value = 1, IsActive = true };
            var bytes = _serializer.SerializeToBytes(payload);

            var roundTrip = Encoding.UTF8.GetString(bytes);
            roundTrip.Should().Contain("name: utf8");
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
            var act = () => _serializer.Deserialize<TestPayload>((string)null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Deserialize_ByType_ReturnsObject()
        {
            var yaml = "name: byType\nvalue: 11\nisActive: true\n";
            var result = _serializer.Deserialize(yaml, typeof(TestPayload)) as TestPayload;

            result.Should().NotBeNull();
            result!.Name.Should().Be("byType");
            result.Value.Should().Be(11);
            result.IsActive.Should().BeTrue();
        }

        [Fact]
        public void Constructor_CustomBuilders_UsesProvidedPipeline()
        {
            var yamlSerializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            var yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var serializer = new YamlDotNetSerializer(yamlSerializer, yamlDeserializer);

            var payload = new TestPayload { Name = "underscored", Value = 5, IsActive = true };
            var yaml = serializer.Serialize(payload);

            yaml.Should().Contain("is_active:");
            yaml.Should().NotContain("isActive:");

            var result = serializer.Deserialize<TestPayload>(yaml);
            result.Should().NotBeNull();
            result!.IsActive.Should().BeTrue();
        }

        [Fact]
        public void Deserialize_UnmatchedProperty_IsIgnoredByDefault()
        {
            var yaml = "name: hello\nvalue: 1\nisActive: true\nunknown: ignoreMe\n";

            var act = () => _serializer.Deserialize<TestPayload>(yaml);
            act.Should().NotThrow();
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

            var yaml = _serializer.Serialize(original);
            var result = _serializer.Deserialize<NestedPayload>(yaml);

            result.Should().NotBeNull();
            result!.Title.Should().Be("parent");
            result.Tags.Should().BeEquivalentTo(new[] { "x", "y" });
            result.Inner.Should().NotBeNull();
            result.Inner!.Name.Should().Be("child");
            result.Inner.Value.Should().Be(3);
        }
    }
}
