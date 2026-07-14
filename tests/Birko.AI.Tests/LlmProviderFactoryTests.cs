using Birko.AI.Factories;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Tests
{
    /// <summary>
    /// Covers LlmProviderFactory (CR-L007 test-gap): Register/Create/IsRegistered/GetRegisteredProviders,
    /// case-insensitive lookup, and the unknown-provider message. The registry is now a
    /// ConcurrentDictionary (CR-L006); these tests use uniquely-named providers so they don't collide
    /// with any built-ins registered elsewhere.
    /// </summary>
    public class LlmProviderFactoryTests
    {
        private sealed class FakeProvider : ILlmProvider
        {
            public string Name => "fake";
            public Action<string, string>? MessageCallback { get; set; }
            public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
                => Task.FromResult(new LlmResponse { Content = new() });
            public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
        }

        [Fact]
        public void Register_ThenCreate_ReturnsInstanceFromFactory()
        {
            var name = "cr-l007-create-" + Guid.NewGuid().ToString("N");
            LlmProviderFactory.Register(name, _ => new FakeProvider());

            var provider = LlmProviderFactory.Create(name);

            provider.Should().BeOfType<FakeProvider>();
        }

        [Fact]
        public void IsRegistered_And_Create_AreCaseInsensitive()
        {
            var name = "CR-L007-Case-" + Guid.NewGuid().ToString("N");
            LlmProviderFactory.Register(name, _ => new FakeProvider());

            LlmProviderFactory.IsRegistered(name.ToLowerInvariant()).Should().BeTrue();
            LlmProviderFactory.IsRegistered(name.ToUpperInvariant()).Should().BeTrue();
            LlmProviderFactory.Create(name.ToUpperInvariant()).Should().BeOfType<FakeProvider>();
        }

        [Fact]
        public void Register_LastWins_ForSameName()
        {
            var name = "cr-l006-lastwins-" + Guid.NewGuid().ToString("N");
            var config = new Dictionary<string, string>();

            LlmProviderFactory.Register(name, _ => throw new InvalidOperationException("first factory"));
            LlmProviderFactory.Register(name, _ => new FakeProvider()); // overwrites

            LlmProviderFactory.Create(name, config).Should().BeOfType<FakeProvider>();
        }

        [Fact]
        public void GetRegisteredProviders_IncludesRegisteredName()
        {
            var name = "cr-l007-list-" + Guid.NewGuid().ToString("N");
            LlmProviderFactory.Register(name, _ => new FakeProvider());

            LlmProviderFactory.GetRegisteredProviders().Should().Contain(name);
        }

        [Fact]
        public void Create_UnknownProvider_ThrowsWithDescriptiveMessage()
        {
            var missing = "cr-l007-missing-" + Guid.NewGuid().ToString("N");

            var act = () => LlmProviderFactory.Create(missing);

            act.Should().Throw<ArgumentException>().WithMessage("*is not registered*");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Register_And_Create_RejectEmptyName(string name)
        {
            var registerAct = () => LlmProviderFactory.Register(name, _ => new FakeProvider());
            registerAct.Should().Throw<ArgumentException>();

            var createAct = () => LlmProviderFactory.Create(name);
            createAct.Should().Throw<ArgumentException>();

            LlmProviderFactory.IsRegistered(name).Should().BeFalse();
        }
    }
}
