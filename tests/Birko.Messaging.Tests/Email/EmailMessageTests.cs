using Birko.Messaging.Email;
using FluentAssertions;
using Xunit;

namespace Birko.Messaging.Tests.Email;

public class EmailMessageTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var msg = new EmailMessage();

        msg.Id.Should().BeNull();
        msg.From.Should().BeNull();
        msg.Recipients.Should().BeEmpty();
        msg.Cc.Should().BeEmpty();
        msg.Bcc.Should().BeEmpty();
        msg.ReplyTo.Should().BeNull();
        msg.Subject.Should().BeEmpty();
        msg.Body.Should().BeEmpty();
        msg.IsHtml.Should().BeFalse();
        msg.PlainTextBody.Should().BeNull();
        msg.Attachments.Should().BeEmpty();
        msg.ScheduledAt.Should().BeNull();
        msg.Metadata.Should().BeEmpty();
        msg.Priority.Should().Be(MessagePriority.Normal);
        msg.Headers.Should().BeEmpty();
    }

    [Fact]
    public void SetProperties_RoundTrips()
    {
        var from = new MessageAddress("sender@test.com", "Sender");
        var to = new MessageAddress("recipient@test.com");
        var cc = new MessageAddress("cc@test.com");
        var bcc = new MessageAddress("bcc@test.com");
        var replyTo = new MessageAddress("reply@test.com");

        var msg = new EmailMessage
        {
            Id = "test-id",
            From = from,
            Recipients = new[] { to },
            Cc = new[] { cc },
            Bcc = new[] { bcc },
            ReplyTo = replyTo,
            Subject = "Test Subject",
            Body = "<p>Hello</p>",
            IsHtml = true,
            PlainTextBody = "Hello",
            Priority = MessagePriority.High,
        };
        msg.Headers["X-Custom"] = "value";
        msg.Metadata["key"] = "val";

        msg.Id.Should().Be("test-id");
        msg.From.Should().BeSameAs(from);
        msg.Recipients.Should().ContainSingle().Which.Should().BeSameAs(to);
        msg.Cc.Should().ContainSingle().Which.Should().BeSameAs(cc);
        msg.Bcc.Should().ContainSingle().Which.Should().BeSameAs(bcc);
        msg.ReplyTo.Should().BeSameAs(replyTo);
        msg.Subject.Should().Be("Test Subject");
        msg.Body.Should().Be("<p>Hello</p>");
        msg.IsHtml.Should().BeTrue();
        msg.PlainTextBody.Should().Be("Hello");
        msg.Priority.Should().Be(MessagePriority.High);
        msg.Headers.Should().ContainKey("X-Custom").WhoseValue.Should().Be("value");
        msg.Metadata.Should().ContainKey("key").WhoseValue.Should().Be("val");
    }
}
