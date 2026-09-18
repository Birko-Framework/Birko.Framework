using System.Linq;
using System.Threading.Tasks;
using Birko.Communication.WebSocket.Ports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.WebSocket.Tests;

/// <summary>
/// Regression for CR-H036: Read(size) supports size &lt; 0 ("read all"), but RemoveReadData(-1)
/// called RemoveRange(0, -1) and threw; the check/read/remove was also non-atomic vs the ReadWorker.
/// Buffer semantics are transport-agnostic — no socket needed.
/// </summary>
public class WebSocketPortBufferTests
{
    private static WebSocketPort NewPort(params byte[] data)
    {
        var port = new WebSocketPort(new WebSocketSettings { Name = "ws" });
        port.ReadData.AddRange(data);
        return port;
    }

    [Fact]
    public void RemoveReadData_All_DrainsWithoutThrowing()
    {
        var port = NewPort(1, 2, 3, 4);
        port.RemoveReadData(-1).Should().Equal(1, 2, 3, 4);
        port.ReadData.Should().BeEmpty();
    }

    [Fact]
    public void RemoveReadData_All_OnEmpty_ReturnsEmpty()
    {
        NewPort().RemoveReadData(-1).Should().BeEmpty();
    }

    [Fact]
    public void RemoveReadData_Partial()
    {
        var port = NewPort(5, 6, 7, 8);
        port.RemoveReadData(2).Should().Equal(5, 6);
        port.ReadData.Should().Equal(7, 8);
    }

    [Fact]
    public void HasReadData_NegativeSemantics()
    {
        NewPort().HasReadData(-1).Should().BeFalse();
        NewPort(1).HasReadData(-1).Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentAppendAndDrain_DoesNotThrow()
    {
        var port = NewPort();
        var producer = Task.Run(() =>
        {
            for (var i = 0; i < 5000; i++)
                lock (port.ReadData) { port.ReadData.AddRange(new byte[] { 1, 2, 3 }); }
        });
        var consumer = Task.Run(() =>
        {
            for (var i = 0; i < 5000; i++)
                port.RemoveReadData(-1);
        });

        var act = async () => await Task.WhenAll(producer, consumer);
        await act.Should().NotThrowAsync();
    }
}
