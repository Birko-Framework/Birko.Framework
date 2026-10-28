using FluentAssertions;
using Xunit;

// Stands in for Birko.Communication.WebSocket, whose root namespace is a sibling of
// Birko.Communication.GraphQL. Inside the GraphQL namespace, name lookup reaches the enclosing
// Birko.Communication namespace before the file's usings, so an unqualified `WebSocket` binds to
// this namespace instead of System.Net.WebSockets.WebSocket (CS0118). Declaring it here makes
// this test project fail to compile if GraphQL uses the bare name again (TASK-780).
namespace Birko.Communication.WebSocket
{
    internal static class NamespaceProbe
    {
        public const string Name = nameof(NamespaceProbe);
    }
}

namespace Birko.Communication.GraphQL.Tests
{
    public class WebSocketNamespaceCollisionTests
    {
        [Fact]
        public void GraphQL_CompilesBeside_TheBirkoCommunicationWebSocketNamespace()
        {
            // The assertion is the build: this file only compiles together with the GraphQL
            // sources when they never bind the bare name `WebSocket`.
            Birko.Communication.WebSocket.NamespaceProbe.Name.Should().Be("NamespaceProbe");
        }
    }
}
