using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Tests.Serialization
{
    public class EncryptingMessageSerializerTests
    {
        // Simple reversible "encryption" for testing (base64 encode/decode)
        private static string FakeEncrypt(string plaintext) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));
        private static string FakeDecrypt(string ciphertext) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext));

        private readonly EncryptingMessageSerializer _serializer = new(
            new JsonMessageSerializer(), FakeEncrypt, FakeDecrypt);

        [Fact]
        public void ContentType_IncludesEncryptedSuffix()
        {
            _serializer.ContentType.Should().Be("application/json+encrypted");
        }

        [Fact]
        public void Serialize_ProducesEncryptedOutput()
        {
            var result = _serializer.Serialize(new TestPayload { Name = "secret", Value = 42 });

            // Should be base64, not raw JSON
            result.Should().NotContain("\"name\"");
            result.Should().NotContain("secret");
        }

        [Fact]
        public void RoundTrip_PreservesData()
        {
            var original = new TestPayload { Name = "encrypted", Value = 999 };
            var encrypted = _serializer.Serialize(original);
            var restored = _serializer.Deserialize<TestPayload>(encrypted);

            restored.Should().NotBeNull();
            restored!.Name.Should().Be("encrypted");
            restored.Value.Should().Be(999);
        }

        [Fact]
        public void Deserialize_ByType_Works()
        {
            var encrypted = _serializer.Serialize(new TestPayload { Name = "typed", Value = 1 });
            var result = _serializer.Deserialize(encrypted, typeof(TestPayload)) as TestPayload;

            result.Should().NotBeNull();
            result!.Name.Should().Be("typed");
        }

        [Fact]
        public void Constructor_ThrowsOnNullInner()
        {
            var act = () => new EncryptingMessageSerializer(null!, FakeEncrypt, FakeDecrypt);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_ThrowsOnNullEncrypt()
        {
            var act = () => new EncryptingMessageSerializer(new JsonMessageSerializer(), null!, FakeDecrypt);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_ThrowsOnNullDecrypt()
        {
            var act = () => new EncryptingMessageSerializer(new JsonMessageSerializer(), FakeEncrypt, null!);
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
