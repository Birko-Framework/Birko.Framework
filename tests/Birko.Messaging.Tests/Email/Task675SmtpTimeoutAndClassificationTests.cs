using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Messaging.Email;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Messaging.Tests.Email;

/// <summary>
/// TASK-675: <see cref="SmtpEmailSender"/> keeps <see cref="EmailSettings.Timeout"/> on the async path, and every failure
/// says whether it is worth retrying. The last group sends through a real <see cref="SmtpClient"/> to an in-process stub
/// server, so the classification is checked against the exceptions the client really throws.
/// </summary>
public sealed class Task675SmtpTimeoutAndClassificationTests
{
    private readonly ITestOutputHelper _output;

    public Task675SmtpTimeoutAndClassificationTests(ITestOutputHelper output) => _output = output;

    private static EmailSettings Settings(int timeout) => new("smtp.test", 25) { Timeout = timeout, UseSecure = false };

    private static EmailMessage Message() => new()
    {
        From = new MessageAddress("from@example.com"),
        Recipients = new[] { new MessageAddress("to@example.com") },
        Subject = "s",
        Body = "b",
    };

    // ---------------------------------------------------------------- the deadline

    [Fact]
    public async Task A_send_that_never_completes_is_abandoned_at_the_timeout_as_a_transient_failure()
    {
        using var sender = new SmtpEmailSender(Settings(300), (_, _) => new TaskCompletionSource().Task);

        var watch = Stopwatch.StartNew();
        var result = await sender.SendAsync(Message());

        watch.ElapsedMilliseconds.Should().BeLessThan(1500, "the 300 ms deadline holds even for a send that ignores its token");
        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(MessageFailureKind.Transient);
        result.IsTransientFailure.Should().BeTrue();
        result.Error.Should().Contain("300 ms");
    }

    [Fact]
    public async Task A_send_that_watches_its_token_is_cancelled_at_the_timeout_too()
    {
        using var sender = new SmtpEmailSender(Settings(300), (_, token) => Task.Delay(Timeout.Infinite, token));

        var result = await sender.SendAsync(Message());

        result.FailureKind.Should().Be(MessageFailureKind.Transient);
    }

    [Fact]
    public async Task The_callers_cancellation_still_throws()
    {
        using var sender = new SmtpEmailSender(Settings(30000), (_, _) => new TaskCompletionSource().Task);
        using var caller = new CancellationTokenSource(200);

        var act = async () => await sender.SendAsync(Message(), caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task No_timeout_means_no_deadline()
    {
        using var sender = new SmtpEmailSender(Settings(0), async (_, _) => await Task.Delay(400));

        var result = await sender.SendAsync(Message());

        result.Success.Should().BeTrue();
        result.FailureKind.Should().Be(MessageFailureKind.None);
    }

    [Fact]
    public async Task An_abandoned_send_holds_the_client_until_it_ends_and_then_releases_it()
    {
        var first = new TaskCompletionSource();
        var calls = 0;
        using var sender = new SmtpEmailSender(Settings(300), (_, _) => Interlocked.Increment(ref calls) == 1 ? first.Task : Task.CompletedTask);

        (await sender.SendAsync(Message())).FailureKind.Should().Be(MessageFailureKind.Transient);
        var whileBusy = await sender.SendAsync(Message());
        whileBusy.FailureKind.Should().Be(MessageFailureKind.Transient, "the client is still busy with the abandoned send, which the deadline covers too");
        first.SetResult();
        var afterwards = await sender.SendAsync(Message());

        afterwards.Success.Should().BeTrue("the abandoned send ended, so the client is free again");
        calls.Should().Be(2, "the send waiting for the busy client never reached it");
    }

    [Fact]
    public async Task A_batch_gives_every_message_its_own_deadline_and_classification()
    {
        var calls = 0;
        using var sender = new SmtpEmailSender(Settings(300), (_, _) => Interlocked.Increment(ref calls) == 1
            ? Task.FromException(new SmtpFailedRecipientException(SmtpStatusCode.MailboxUnavailable, "to@example.com"))
            : Task.CompletedTask);

        var results = await sender.SendBatchAsync(new[] { Message(), null!, Message() });

        results.Select(r => r.FailureKind).Should().Equal(MessageFailureKind.Permanent, MessageFailureKind.Permanent, MessageFailureKind.None);
    }

    [Fact]
    public async Task Failures_the_sender_finds_itself_are_permanent()
    {
        using var sender = new SmtpEmailSender(Settings(1000), (_, _) => Task.CompletedTask);

        (await sender.SendAsync(new EmailMessage { From = new MessageAddress("f@example.com"), Subject = "s", Body = "b" }))
            .FailureKind.Should().Be(MessageFailureKind.Permanent, "no recipients");
        (await sender.SendAsync(new EmailMessage { Recipients = new[] { new MessageAddress("t@example.com") }, Subject = "s", Body = "b" }))
            .FailureKind.Should().Be(MessageFailureKind.Permanent, "no sender");
        (await sender.SendAsync(new EmailMessage { From = new MessageAddress("f@example.com"), Recipients = new[] { new MessageAddress("not an address") }, Subject = "s", Body = "b" }))
            .FailureKind.Should().Be(MessageFailureKind.Permanent, "a malformed recipient");
    }

    [Fact]
    public void A_failed_result_cannot_claim_no_failure()
    {
        var act = () => MessageResult.Failed("x", kind: MessageFailureKind.None);

        act.Should().Throw<ArgumentException>();
        MessageResult.Failed("x").FailureKind.Should().Be(MessageFailureKind.Unknown, "a sender that cannot tell says so");
    }

    // ---------------------------------------------------------------- the classifier

    [Theory]
    [InlineData(SmtpStatusCode.MailboxUnavailable, MessageFailureKind.Permanent)]
    [InlineData(SmtpStatusCode.MailboxNameNotAllowed, MessageFailureKind.Permanent)]
    [InlineData(SmtpStatusCode.UserNotLocalTryAlternatePath, MessageFailureKind.Permanent)]
    [InlineData(SmtpStatusCode.MailboxBusy, MessageFailureKind.Transient)]
    [InlineData(SmtpStatusCode.InsufficientStorage, MessageFailureKind.Transient)]
    public void A_refused_recipient_is_classified_by_its_reply_code(SmtpStatusCode code, MessageFailureKind expected)
        => SmtpFailureClassifier.Classify(new SmtpFailedRecipientException(code, "to@example.com")).Should().Be(expected);

    [Fact]
    public void Several_refused_recipients_are_worth_retrying_only_if_one_refusal_was_temporary()
    {
        var permanent = new SmtpFailedRecipientException(SmtpStatusCode.MailboxUnavailable, "a@example.com");
        var temporary = new SmtpFailedRecipientException(SmtpStatusCode.MailboxBusy, "b@example.com");

        SmtpFailureClassifier.Classify(new SmtpFailedRecipientsException("x", new[] { permanent, permanent }))
            .Should().Be(MessageFailureKind.Permanent);
        SmtpFailureClassifier.Classify(new SmtpFailedRecipientsException("x", new[] { permanent, temporary }))
            .Should().Be(MessageFailureKind.Transient);
    }

    [Fact]
    public void An_smtp_failure_without_a_reply_is_classified_by_its_inner_exception()
    {
        SmtpFailureClassifier.Classify(new SmtpException("Failure sending mail.", new SocketException((int)SocketError.ConnectionRefused)))
            .Should().Be(MessageFailureKind.Transient);
        SmtpFailureClassifier.Classify(new SmtpException("Failure sending mail.", new IOException("Unable to read data")))
            .Should().Be(MessageFailureKind.Transient);
        SmtpFailureClassifier.Classify(new SmtpException("Failure sending mail.", new InvalidOperationException("?")))
            .Should().Be(MessageFailureKind.Unknown, "nothing says it is transient or permanent");
        SmtpFailureClassifier.Classify(new SmtpException(SmtpStatusCode.ServiceNotAvailable)).Should().Be(MessageFailureKind.Transient);
        SmtpFailureClassifier.Classify(new SmtpException(SmtpStatusCode.TransactionFailed)).Should().Be(MessageFailureKind.Permanent);
        SmtpFailureClassifier.Classify(new InvalidRecipientException("x")).Should().Be(MessageFailureKind.Permanent);
    }

    // ---------------------------------------------------------------- a real SmtpClient against a stub server

    [Fact]
    public async Task Real_client_a_refused_connection_is_transient()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var sender = new SmtpEmailSender(new EmailSettings("127.0.0.1", port) { Timeout = 5000, UseSecure = false });

        var result = await sender.SendAsync(Message());

        _output.WriteLine($"{result.Exception?.GetType().Name}: {result.Exception?.Message} / inner {result.Exception?.InnerException?.GetType().Name}");
        result.FailureKind.Should().Be(MessageFailureKind.Transient);
    }

    [Fact]
    public async Task Real_client_a_server_that_never_answers_is_abandoned_at_the_timeout()
    {
        using var stub = new StubSmtpServer(StubSmtpServer.Mode.Silent);
        using var sender = new SmtpEmailSender(new EmailSettings("127.0.0.1", stub.Port) { Timeout = 1000, UseSecure = false });

        var watch = Stopwatch.StartNew();
        var result = await sender.SendAsync(Message());

        _output.WriteLine($"{watch.ElapsedMilliseconds} ms; {result.Exception?.GetType().Name}: {result.Exception?.Message}");
        watch.ElapsedMilliseconds.Should().BeLessThan(2500, "Timeout = 1000 is the deadline of the whole send");
        result.FailureKind.Should().Be(MessageFailureKind.Transient);
    }

    [Fact]
    public async Task Real_client_a_service_refusal_at_greeting_is_transient()
    {
        using var stub = new StubSmtpServer(StubSmtpServer.Mode.Greet421);
        using var sender = new SmtpEmailSender(new EmailSettings("127.0.0.1", stub.Port) { Timeout = 5000, UseSecure = false });

        var result = await sender.SendAsync(Message());

        _output.WriteLine($"{result.Exception?.GetType().Name}: {(result.Exception as SmtpException)?.StatusCode} {result.Exception?.Message}");
        result.FailureKind.Should().Be(MessageFailureKind.Transient);
    }

    [Fact]
    public async Task Real_client_an_unknown_mailbox_is_permanent()
    {
        using var stub = new StubSmtpServer(StubSmtpServer.Mode.Rcpt550);
        using var sender = new SmtpEmailSender(new EmailSettings("127.0.0.1", stub.Port) { Timeout = 5000, UseSecure = false });

        var result = await sender.SendAsync(Message());

        _output.WriteLine($"{result.Exception?.GetType().Name}: {(result.Exception as SmtpException)?.StatusCode} {result.Exception?.Message}");
        result.Exception.Should().BeAssignableTo<SmtpFailedRecipientException>();
        result.FailureKind.Should().Be(MessageFailureKind.Permanent);
    }

    /// <summary>A loopback SMTP server that answers in one fixed way, enough for SmtpClient to fail as it would.</summary>
    private sealed class StubSmtpServer : IDisposable
    {
        public enum Mode { Silent, Greet421, Rcpt550 }

        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Mode _mode;

        public StubSmtpServer(Mode mode)
        {
            _mode = mode;
            _listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = Task.Run(() => ServeAsync(client));
                }
            }
            catch (Exception)
            {
                // Stopped.
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using var _ = client;
            try
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII);
                var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                switch (_mode)
                {
                    case Mode.Silent:
                        await Task.Delay(Timeout.Infinite, _stop.Token);
                        return;
                    case Mode.Greet421:
                        await writer.WriteLineAsync("421 4.3.2 Service not available, closing channel");
                        return;
                }
                await writer.WriteLineAsync("220 stub ESMTP");
                while (await reader.ReadLineAsync(_stop.Token) is string line)
                {
                    var verb = line.Split(' ', ':')[0].ToUpperInvariant();
                    var reply = verb switch
                    {
                        "EHLO" or "HELO" => "250 stub",
                        "MAIL" => "250 2.1.0 OK",
                        "RCPT" => "550 5.1.1 No such user here",
                        "RSET" => "250 2.0.0 OK",
                        "QUIT" => "221 2.0.0 Bye",
                        _ => "502 5.5.2 Not implemented",
                    };
                    await writer.WriteLineAsync(reply);
                    if (verb == "QUIT") return;
                }
            }
            catch (Exception)
            {
                // The client went away or the stub stopped.
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }
    }
}
