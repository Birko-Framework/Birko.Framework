using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Tests.Core
{
    public class MessageFingerprintTests
    {
        [Fact]
        public void Compute_SameBody_SameFingerprint()
        {
            var fp1 = MessageFingerprint.Compute("hello world");
            var fp2 = MessageFingerprint.Compute("hello world");

            fp1.Should().Be(fp2);
        }

        [Fact]
        public void Compute_DifferentBody_DifferentFingerprint()
        {
            var fp1 = MessageFingerprint.Compute("hello");
            var fp2 = MessageFingerprint.Compute("world");

            fp1.Should().NotBe(fp2);
        }

        [Fact]
        public void Compute_ReturnsHexString()
        {
            var fp = MessageFingerprint.Compute("test");

            fp.Should().MatchRegex("^[0-9a-f]{64}$"); // SHA256 = 64 hex chars
        }

        [Fact]
        public void Compute_QueueMessage_UsesBody()
        {
            var message = new QueueMessage { Body = "test payload" };
            var fp1 = MessageFingerprint.Compute(message);
            var fp2 = MessageFingerprint.Compute("test payload");

            fp1.Should().Be(fp2);
        }

        [Fact]
        public void Compute_WithDestination_DiffersFromBodyOnly()
        {
            var bodyOnly = MessageFingerprint.Compute("payload");
            var withDest = MessageFingerprint.Compute("orders", "payload");

            bodyOnly.Should().NotBe(withDest);
        }

        [Fact]
        public void Compute_SameDestinationAndBody_SameFingerprint()
        {
            var fp1 = MessageFingerprint.Compute("orders", "payload");
            var fp2 = MessageFingerprint.Compute("orders", "payload");

            fp1.Should().Be(fp2);
        }

        [Fact]
        public void Compute_DifferentDestination_DifferentFingerprint()
        {
            var fp1 = MessageFingerprint.Compute("orders", "payload");
            var fp2 = MessageFingerprint.Compute("invoices", "payload");

            fp1.Should().NotBe(fp2);
        }
    }
}
