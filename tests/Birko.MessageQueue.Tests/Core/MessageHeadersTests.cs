using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Tests.Core
{
    public class MessageHeadersTests
    {
        [Fact]
        public void Constructor_SetsDefaults()
        {
            var headers = new MessageHeaders();

            headers.CorrelationId.Should().BeNull();
            headers.ReplyTo.Should().BeNull();
            headers.ContentType.Should().Be("application/json");
            headers.GroupId.Should().BeNull();
            headers.Custom.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void Custom_CanAddEntries()
        {
            var headers = new MessageHeaders();
            headers.Custom["source"] = "test";
            headers.Custom["version"] = "2";

            headers.Custom.Should().HaveCount(2);
            headers.Custom["source"].Should().Be("test");
        }
    }
}
