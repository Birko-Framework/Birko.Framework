using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Birko.Messaging.Email;
using FluentAssertions;
using Xunit;

namespace Birko.Messaging.Tests.Email;

/// <summary>
/// TASK-585: the MIME <see cref="SmtpEmailSender"/> actually puts on the wire. Each test sends through a real
/// <see cref="SmtpClient"/> into a pickup directory and parses the written .eml. Never assert on
/// <see cref="MailMessage"/> properties here: with an AlternateView present, <c>IsBodyHtml</c> reads true while the
/// wire says text/plain, which is the defect these tests exist for.
/// </summary>
public sealed class SmtpEmailSenderWireTests : IDisposable
{
    private const string Html = "<p>Objednávka č. 12 – ďakujeme</p><img src=\"cid:logo\">";
    private const string Plain = "Objednávka č. 12 – ďakujeme";

    private readonly string _pickup = Path.Combine(Path.GetTempPath(), "birko-smtp-wire-" + Guid.NewGuid().ToString("N"));

    public SmtpEmailSenderWireTests() => Directory.CreateDirectory(_pickup);

    public void Dispose()
    {
        try { Directory.Delete(_pickup, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task BothBodies_AreAlternative_PlainFirst_HtmlLast_BothUtf8()
    {
        var root = await SendAsync(new EmailMessage { Body = Html, IsHtml = true, PlainTextBody = Plain });

        root.MediaType.Should().Be("multipart/alternative");
        root.Children.Select(p => p.MediaType).Should().Equal(new[] { "text/plain", "text/html" },
            "RFC 2046: the last alternative is the preferred one, so HTML goes last");
        root.Children.Select(p => p.Charset).Should().Equal("utf-8", "utf-8");
        root.Children[0].DecodedText().Should().Be(Plain);
        root.Children[1].DecodedText().Should().Be(Html);
    }

    [Fact]
    public async Task HtmlOnly_IsOneTextHtmlPart()
    {
        var root = await SendAsync(new EmailMessage { Body = Html, IsHtml = true });

        root.MediaType.Should().Be("text/html");
        root.Children.Should().BeEmpty();
        root.DecodedText().Should().Be(Html);
    }

    [Fact]
    public async Task PlainOnly_IsOneTextPlainPart_EvenWithAPlainTextBodySet()
    {
        // PlainTextBody is the alternative to an HTML body; without IsHtml there is nothing to be an alternative to.
        var root = await SendAsync(new EmailMessage { Body = Plain, IsHtml = false, PlainTextBody = "ignored" });

        root.MediaType.Should().Be("text/plain");
        root.Children.Should().BeEmpty();
        root.DecodedText().Should().Be(Plain);
    }

    [Fact]
    public async Task BothBodies_InlineImage_IsRelatedToTheHtmlPart_SoCidResolves()
    {
        // TASK-585's decision: alternative{ text/plain, related{ text/html, image } }, because RFC 2387 scopes cid: to
        // multipart/related; as a sibling Attachment the image would sit in multipart/mixed.
        var root = await SendAsync(new EmailMessage
        {
            Body = Html, IsHtml = true, PlainTextBody = Plain,
            Attachments = new[] { new MessageAttachment("logo.png", "image/png", new MemoryStream(new byte[] { 137, 80, 78, 71 }), isInline: true, contentId: "logo") },
        });

        root.MediaType.Should().Be("multipart/alternative");
        root.Children.Select(p => p.MediaType).Should().Equal("text/plain", "multipart/related");
        var related = root.Children[1];
        related.Children.Select(p => p.MediaType).Should().Equal("text/html", "image/png");
        related.Children[0].DecodedText().Should().Contain("cid:logo");
        related.Children[1].Header("content-id").Should().Be("<logo>");
    }

    [Fact]
    public async Task BothBodies_RegularAttachment_StaysBesideTheAlternative()
    {
        var root = await SendAsync(new EmailMessage
        {
            Body = Html, IsHtml = true, PlainTextBody = Plain,
            Attachments = new[] { new MessageAttachment("report.pdf", "application/pdf", new MemoryStream(new byte[] { 37, 80, 68, 70 })) },
        });

        root.MediaType.Should().Be("multipart/mixed");
        root.Children.Select(p => p.MediaType).Should().Equal("multipart/alternative", "application/pdf");
        root.Children[0].Children.Select(p => p.MediaType).Should().Equal("text/plain", "text/html");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<MimePart> SendAsync(EmailMessage message)
    {
        message.From = new MessageAddress("from@test.com");
        message.Recipients = new[] { new MessageAddress("to@test.com") };
        message.Subject = "wire";
        var settings = new EmailSettings("localhost", 25);
        using var sender = new SmtpEmailSender(settings, async (mail, ct) =>
        {
            using var client = new SmtpClient("localhost")
            {
                DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                PickupDirectoryLocation = _pickup,
            };
            await client.SendMailAsync(mail, ct);
        });

        var result = await sender.SendAsync(message);
        result.Success.Should().BeTrue(result.Error);
        var eml = Directory.GetFiles(_pickup, "*.eml").Should().ContainSingle().Subject;
        return MimePart.Parse(File.ReadAllText(eml, Encoding.ASCII));
    }

    /// <summary>A minimal MIME reader: headers, media type and parameters, nested multiparts, decoded text.</summary>
    private sealed class MimePart
    {
        private readonly Dictionary<string, string> _headers;
        private readonly string _body;

        public string MediaType { get; }
        public string? Charset { get; }
        public List<MimePart> Children { get; } = new();

        private MimePart(Dictionary<string, string> headers, string body)
        {
            _headers = headers;
            _body = body;
            var contentType = Header("content-type") ?? "text/plain; charset=us-ascii";
            MediaType = contentType.Split(';')[0].Trim().ToLowerInvariant();
            Charset = Param(contentType, "charset")?.ToLowerInvariant();
            if (!MediaType.StartsWith("multipart/", StringComparison.Ordinal)) return;

            var boundary = Param(contentType, "boundary") ?? throw new InvalidOperationException("multipart without a boundary");
            var segments = body.Split("--" + boundary);
            // segments[0] is the preamble; the last one starts with "--" (the closing delimiter).
            foreach (var segment in segments.Skip(1).TakeWhile(s => !s.StartsWith("--", StringComparison.Ordinal)))
                Children.Add(Parse(segment.TrimStart('\r', '\n')));
        }

        public string? Header(string name) => _headers.TryGetValue(name, out var v) ? v : null;

        public static MimePart Parse(string text)
        {
            var split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var head = split < 0 ? text : text[..split];
            var body = split < 0 ? string.Empty : text[(split + 4)..];
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? last = null;
            foreach (var line in head.Split("\r\n"))
            {
                if (line.Length > 0 && char.IsWhiteSpace(line[0]) && last != null) { headers[last] += " " + line.Trim(); continue; }
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                last = line[..colon].Trim();
                headers[last] = line[(colon + 1)..].Trim();
            }
            return new MimePart(headers, body);
        }

        public string DecodedText()
        {
            var cte = (Header("content-transfer-encoding") ?? "7bit").ToLowerInvariant();
            var bytes = cte switch
            {
                "base64" => Convert.FromBase64String(Regex.Replace(_body, @"\s", "")),
                "quoted-printable" => DecodeQuotedPrintable(_body),
                _ => Encoding.ASCII.GetBytes(_body),
            };
            return Encoding.GetEncoding(Charset ?? "us-ascii").GetString(bytes).TrimEnd('\r', '\n');
        }

        private static string? Param(string headerValue, string name)
        {
            var m = Regex.Match(headerValue, $@";\s*{name}\s*=\s*(""(?<v>[^""]*)""|(?<v>[^;\s]+))", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["v"].Value : null;
        }

        private static byte[] DecodeQuotedPrintable(string s)
        {
            s = s.Replace("=\r\n", "");
            var bytes = new List<byte>(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '=' && i + 2 < s.Length)
                {
                    bytes.Add(Convert.ToByte(s.Substring(i + 1, 2), 16));
                    i += 2;
                }
                else bytes.Add((byte)s[i]);
            }
            return bytes.ToArray();
        }
    }
}
