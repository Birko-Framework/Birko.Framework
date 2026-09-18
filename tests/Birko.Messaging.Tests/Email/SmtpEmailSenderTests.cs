using System;
using System.Threading.Tasks;
using Birko.Messaging.Email;
using FluentAssertions;
using Xunit;

namespace Birko.Messaging.Tests.Email;

public class SmtpEmailSenderTests
{
    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        var act = () => new SmtpEmailSender(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Constructor_NullHost_ThrowsArgumentException()
    {
        var settings = new EmailSettings { };

        var act = () => new SmtpEmailSender(settings);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task SendAsync_NullMessage_ThrowsArgumentNullException()
    {
        using var sender = CreateSender();

        var act = () => sender.SendAsync((EmailMessage)null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("message");
    }

    [Fact]
    public async Task SendAsync_EmptyRecipients_ReturnsFailed()
    {
        using var sender = CreateSender();
        var message = new EmailMessage
        {
            From = new MessageAddress("sender@test.com"),
            Recipients = Array.Empty<MessageAddress>(),
            Subject = "Test",
            Body = "Body"
        };

        var result = await sender.SendAsync(message);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No recipients");
    }

    [Fact]
    public async Task SendAsync_NoFromAndNoDefault_ReturnsFailed()
    {
        using var sender = CreateSender();
        var message = new EmailMessage
        {
            Recipients = new[] { new MessageAddress("to@test.com") },
            Subject = "Test",
            Body = "Body"
        };

        var result = await sender.SendAsync(message);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No sender address");
    }

    [Fact]
    public async Task SendAsync_Convenience_BuildsEmailMessage()
    {
        // This test validates that the convenience method doesn't throw for parameter construction.
        // Actual sending would fail (no real SMTP server), but we verify the message is built correctly
        // by checking that it returns a failed result (connection refused), not an argument error.
        using var sender = CreateSender();
        var from = new MessageAddress("from@test.com");
        var to = new MessageAddress("to@test.com");

        var result = await sender.SendAsync(from, to, "Subject", "Body");

        // Will fail because no SMTP server, but it should NOT be an argument/recipient error
        result.Success.Should().BeFalse();
        result.Error.Should().NotContain("No recipients");
        result.Error.Should().NotContain("No sender address");
    }

    [Fact]
    public async Task SendBatchAsync_NullMessages_ThrowsArgumentNullException()
    {
        using var sender = CreateSender();

        var act = () => sender.SendBatchAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messages");
    }

    [Fact]
    public async Task SendBatchAsync_EmptyList_ReturnsEmptyResults()
    {
        using var sender = CreateSender();

        var results = await sender.SendBatchAsync(Array.Empty<EmailMessage>());

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SendBatchAsync_NullElement_CapturedAsFailed_OthersSucceed()
    {
        // CR-L295: a null element must yield a Failed result, not abort the batch and discard the rest.
        var settings = new EmailSettings("localhost", 25) { DefaultFrom = new MessageAddress("from@test.com") };
        using var sender = new SmtpEmailSender(settings, (m, ct) => Task.CompletedTask);
        var good = new EmailMessage
        {
            From = new MessageAddress("from@test.com"),
            Recipients = new[] { new MessageAddress("to@test.com") },
            Subject = "s",
            Body = "b"
        };

        var results = await sender.SendBatchAsync(new[] { good, null!, good });

        results.Should().HaveCount(3);
        results[0].Success.Should().BeTrue();
        results[1].Success.Should().BeFalse();
        results[1].Error.Should().Contain("Null message");
        results[2].Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_EmptyRecipientAddress_ReturnsInvalidRecipientFailure()
    {
        // CR-L296: an empty address value surfaces the dedicated InvalidRecipient reason, not the generic one.
        var settings = new EmailSettings("localhost", 25);
        using var sender = new SmtpEmailSender(settings, (m, ct) => Task.CompletedTask);
        var message = new EmailMessage
        {
            From = new MessageAddress("from@test.com"),
            Recipients = new[] { new MessageAddress("") },
            Subject = "s",
            Body = "b"
        };

        var result = await sender.SendAsync(message);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid recipient");
    }

    [Fact]
    public async Task SendAsync_MalformedRecipientAddress_ReturnsInvalidRecipientFailure()
    {
        var settings = new EmailSettings("localhost", 25);
        using var sender = new SmtpEmailSender(settings, (m, ct) => Task.CompletedTask);
        var message = new EmailMessage
        {
            From = new MessageAddress("from@test.com"),
            Recipients = new[] { new MessageAddress("not-an-email") },
            Subject = "s",
            Body = "b"
        };

        var result = await sender.SendAsync(message);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid recipient");
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var sender = CreateSender();

        var act = () => sender.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task SendAsync_ConcurrentCalls_AreSerialized()
    {
        // CR-M210: SmtpClient is not concurrency-safe; the sender must serialize sends. The test-seam
        // ctor tracks how many sends overlap — with the SemaphoreSlim it must never exceed one.
        var settings = new EmailSettings("localhost", 25) { DefaultFrom = new MessageAddress("from@test.com") };
        var current = 0;
        var max = 0;
        var gate = new object();
        System.Func<System.Net.Mail.MailMessage, System.Threading.CancellationToken, Task> send = async (m, ct) =>
        {
            var c = System.Threading.Interlocked.Increment(ref current);
            lock (gate) { if (c > max) max = c; }
            await Task.Delay(30, ct);
            System.Threading.Interlocked.Decrement(ref current);
        };
        using var sender = new SmtpEmailSender(settings, send);

        var tasks = System.Linq.Enumerable.Range(0, 6).Select(_ => sender.SendAsync(new EmailMessage
        {
            From = new MessageAddress("from@test.com"),
            Recipients = new[] { new MessageAddress("to@test.com") },
            Subject = "s",
            Body = "b"
        }));
        await Task.WhenAll(tasks);

        max.Should().Be(1, "SmtpClient sends must be serialized so concurrent SendAsync never overlaps");
    }

    private static SmtpEmailSender CreateSender()
    {
        var settings = new EmailSettings("localhost", 25);
        return new SmtpEmailSender(settings);
    }
}
