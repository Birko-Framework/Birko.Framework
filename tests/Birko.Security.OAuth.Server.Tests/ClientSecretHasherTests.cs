using Birko.Security.OAuth.Server.Internal;
using FluentAssertions;
using Xunit;

namespace Birko.Security.OAuth.Server.Tests;

public class ClientSecretHasherTests
{
    [Fact]
    public void Hash_IsDeterministic()
    {
        ClientSecretHasher.Hash("secret").Should().Be(ClientSecretHasher.Hash("secret"));
    }

    [Fact]
    public void Hash_DifferentInputs_DifferentOutputs()
    {
        ClientSecretHasher.Hash("a").Should().NotBe(ClientSecretHasher.Hash("b"));
    }

    [Fact]
    public void Verify_AcceptsMatching()
    {
        var hash = ClientSecretHasher.Hash("hunter2");
        ClientSecretHasher.Verify("hunter2", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_RejectsMismatch()
    {
        var hash = ClientSecretHasher.Hash("hunter2");
        ClientSecretHasher.Verify("Hunter2", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_EmptyInputs_ReturnsFalse()
    {
        ClientSecretHasher.Verify("", "abc").Should().BeFalse();
        ClientSecretHasher.Verify("abc", "").Should().BeFalse();
    }
}
