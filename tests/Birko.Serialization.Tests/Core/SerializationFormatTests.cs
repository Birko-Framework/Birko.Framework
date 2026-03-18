using FluentAssertions;
using Xunit;

namespace Birko.Serialization.Tests.Core
{
    public class SerializationFormatTests
    {
        [Fact]
        public void Enum_HasExpectedValues()
        {
            Enum.GetValues<SerializationFormat>().Should().HaveCount(4);
            SerializationFormat.Json.Should().BeDefined();
            SerializationFormat.MessagePack.Should().BeDefined();
            SerializationFormat.Protobuf.Should().BeDefined();
            SerializationFormat.Xml.Should().BeDefined();
        }
    }
}
