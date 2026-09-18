using System.Linq;
using System.Threading.Tasks;
using Birko.Communication.Network.Ports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.Network.Tests;

/// <summary>
/// CR-H026 (ReadData accessed without a lock, racing the background reader) and CR-H027
/// (RemoveReadData(-1) threw). Buffer semantics are transport-agnostic — no socket needed.
/// </summary>
public class NetworkPortBufferTests
{
    private static TcpIp NewTcp(params byte[] data)
    {
        var port = new TcpIp(new TcpIpSettings { Name = "t" });
        port.ReadData.AddRange(data);
        return port;
    }

    private static Udp NewUdp(params byte[] data)
    {
        var port = new Udp(new UdpSettings { Name = "u" });
        port.ReadData.AddRange(data);
        return port;
    }

    [Fact]
    public void Tcp_RemoveReadData_All_DrainsWithoutThrowing()
    {
        var port = NewTcp(1, 2, 3);
        port.RemoveReadData(-1).Should().Equal(1, 2, 3);
        port.ReadData.Should().BeEmpty();
    }

    [Fact]
    public void Udp_RemoveReadData_All_DrainsWithoutThrowing()
    {
        var port = NewUdp(9, 8, 7, 6);
        port.RemoveReadData(-1).Should().Equal(9, 8, 7, 6);
        port.ReadData.Should().BeEmpty();
    }

    [Fact]
    public void Tcp_RemoveReadData_Partial()
    {
        var port = NewTcp(1, 2, 3, 4);
        port.RemoveReadData(2).Should().Equal(1, 2);
        port.ReadData.Should().Equal(3, 4);
    }

    [Fact]
    public void Tcp_HasReadData_NegativeSemantics()
    {
        NewTcp().HasReadData(-1).Should().BeFalse();
        NewTcp(1).HasReadData(-1).Should().BeTrue();
    }

    [Fact]
    public async Task Tcp_ConcurrentAppendAndDrain_DoesNotThrow()
    {
        // CR-H026: a background thread appending to ReadData under lock while the consumer drains
        // must not tear or throw. Hammer both sides concurrently and require completion.
        var port = NewTcp();

        var producer = Task.Run(() =>
        {
            for (var i = 0; i < 5000; i++)
                lock (port.ReadData) { port.ReadData.AddRange(new byte[] { 1, 2, 3, 4 }); }
        });

        var consumer = Task.Run(() =>
        {
            for (var i = 0; i < 5000; i++)
                port.RemoveReadData(-1); // read+remove under the same lock
        });

        var act = async () => await Task.WhenAll(producer, consumer);
        await act.Should().NotThrowAsync();
    }
}
