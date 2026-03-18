using System;
using System.Xml;
using System.Xml.Serialization;
using Birko.Serialization.Xml;
using FluentAssertions;
using Xunit;

namespace Birko.Serialization.Tests.Xml
{
    public class XmlTestPayload
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public bool IsActive { get; set; }
    }

    public class XmlNestedPayload
    {
        public string Title { get; set; } = string.Empty;

        [XmlArray("Tags")]
        [XmlArrayItem("Tag")]
        public List<string> Tags { get; set; } = new();

        public XmlTestPayload? Inner { get; set; }
    }

    public class SystemXmlSerializerTests
    {
        private readonly SystemXmlSerializer _serializer = new();

        [Fact]
        public void ContentType_ReturnsApplicationXml()
        {
            _serializer.ContentType.Should().Be("application/xml");
        }

        [Fact]
        public void Format_ReturnsXml()
        {
            _serializer.Format.Should().Be(SerializationFormat.Xml);
        }

        [Fact]
        public void Serialize_Object_ReturnsXmlString()
        {
            var payload = new XmlTestPayload { Name = "test", Value = 42, IsActive = true };
            var xml = _serializer.Serialize((object)payload);

            xml.Should().Contain("<Name>test</Name>");
            xml.Should().Contain("<Value>42</Value>");
            xml.Should().Contain("<IsActive>true</IsActive>");
        }

        [Fact]
        public void Serialize_Generic_ReturnsXmlString()
        {
            var payload = new XmlTestPayload { Name = "test", Value = 42, IsActive = true };
            var xml = _serializer.Serialize(payload);

            xml.Should().Contain("<Name>test</Name>");
            xml.Should().Contain("<?xml");
        }

        [Fact]
        public void RoundTrip_String_PreservesData()
        {
            var original = new XmlTestPayload { Name = "roundtrip", Value = 99, IsActive = false };
            var xml = _serializer.Serialize(original);
            var result = _serializer.Deserialize<XmlTestPayload>(xml);

            result.Should().NotBeNull();
            result!.Name.Should().Be(original.Name);
            result.Value.Should().Be(original.Value);
            result.IsActive.Should().Be(original.IsActive);
        }

        [Fact]
        public void RoundTrip_Bytes_PreservesData()
        {
            var original = new XmlTestPayload { Name = "bytes", Value = 7, IsActive = true };
            var bytes = _serializer.SerializeToBytes(original);
            var result = _serializer.DeserializeFromBytes<XmlTestPayload>(bytes);

            result.Should().NotBeNull();
            result!.Name.Should().Be(original.Name);
            result.Value.Should().Be(original.Value);
            result.IsActive.Should().Be(original.IsActive);
        }

        [Fact]
        public void Deserialize_ByType_ReturnsObject()
        {
            var original = new XmlTestPayload { Name = "typed", Value = 55, IsActive = true };
            var xml = _serializer.Serialize(original);
            var result = _serializer.Deserialize(xml, typeof(XmlTestPayload)) as XmlTestPayload;

            result.Should().NotBeNull();
            result!.Name.Should().Be("typed");
            result.Value.Should().Be(55);
        }

        [Fact]
        public void DeserializeFromBytes_ByType_ReturnsObject()
        {
            var original = new XmlTestPayload { Name = "test", Value = 42, IsActive = true };
            var bytes = _serializer.SerializeToBytes(original);
            var result = _serializer.DeserializeFromBytes(bytes, typeof(XmlTestPayload)) as XmlTestPayload;

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
            var act = () => _serializer.Deserialize<XmlTestPayload>(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void SerializeToBytes_NullValue_ThrowsArgumentNullException()
        {
            var act = () => _serializer.SerializeToBytes((object)null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void DeserializeFromBytes_NullData_ThrowsArgumentNullException()
        {
            var act = () => _serializer.DeserializeFromBytes<XmlTestPayload>(null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Serialize_NestedObject_PreservesStructure()
        {
            var original = new XmlNestedPayload
            {
                Title = "parent",
                Tags = new List<string> { "a", "b" },
                Inner = new XmlTestPayload { Name = "child", Value = 5, IsActive = true }
            };

            var xml = _serializer.Serialize(original);
            var result = _serializer.Deserialize<XmlNestedPayload>(xml);

            result.Should().NotBeNull();
            result!.Title.Should().Be("parent");
            result.Tags.Should().BeEquivalentTo(new[] { "a", "b" });
            result.Inner.Should().NotBeNull();
            result.Inner!.Name.Should().Be("child");
        }

        [Fact]
        public void Constructor_CustomWriterSettings_UsesProvidedSettings()
        {
            var writerSettings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true
            };
            var serializer = new SystemXmlSerializer(writerSettings);

            var payload = new XmlTestPayload { Name = "test", Value = 1, IsActive = true };
            var xml = serializer.Serialize(payload);

            xml.Should().Contain("\n");
            xml.Should().NotStartWith("<?xml");
        }

        [Fact]
        public void SerializeToBytes_ProducesUtf8()
        {
            var payload = new XmlTestPayload { Name = "test", Value = 1, IsActive = true };
            var bytes = _serializer.SerializeToBytes(payload);

            bytes.Should().NotBeEmpty();
            var xml = System.Text.Encoding.UTF8.GetString(bytes);
            xml.Should().Contain("<Name>test</Name>");
        }
    }
}
