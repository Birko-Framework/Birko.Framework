using Birko.Security.Encryption;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>
/// TASK-176 — SH-L230: a key could be made only through the static <c>AesEncryptionProvider.GenerateKey()</c>, so a caller
/// holding <see cref="IEncryptionProvider"/> could not get one without knowing the implementation. The interface now
/// creates its own keys.
/// </summary>
public class Task176EncryptionKeyTests
{
    [Fact]
    public void The_interface_makes_a_key_its_provider_accepts()
    {
        IEncryptionProvider provider = new AesEncryptionProvider();

        var key = provider.CreateKey();

        key.Should().HaveCount(32);
        provider.DecryptString(provider.EncryptString("secret", key), key).Should().Be("secret");
        provider.CreateKey().Should().NotEqual(key, "each key is fresh randomness");
    }
}
