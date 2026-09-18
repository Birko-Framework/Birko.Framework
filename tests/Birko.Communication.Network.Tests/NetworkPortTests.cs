using Birko.Communication.Network.Ports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.Network.Tests;

/// <summary>
/// Offline tests for the <see cref="TcpIp"/> / <see cref="Udp"/> ports (CR-M055 — no test project).
/// GetID formatting + buffer semantics (Read / HasReadData / RemoveReadData) are transport-agnostic:
/// they operate on the public <c>ReadData</c> list under <c>lock(ReadData)</c> and never touch a
/// socket, so they are exercised on a seeded buffer without calling Open()/Write()/Close().
/// </summary>
public class NetworkPortTests
{
    private static TcpIp NewTcp(params byte[] data)
    {
        var port = new TcpIp(new TcpIpSettings { Name = "t", Address = "127.0.0.1", Port = 5000 });
        port.ReadData.AddRange(data);
        return port;
    }

    private static Udp NewUdp(params byte[] data)
    {
        var port = new Udp(new UdpSettings { Name = "u", Address = "127.0.0.1", Port = 5000, LocalPort = 6000 });
        port.ReadData.AddRange(data);
        return port;
    }

    // ---- GetID formatting ----

    [Fact]
    public void TcpIpSettings_GetID_FormatsAsExpected()
    {
        // Source: "TcpIp|{Name}|{Address}|{Port}"
        new TcpIpSettings { Name = "sensor", Address = "10.0.0.5", Port = 502 }
            .GetID().Should().Be("TcpIp|sensor|10.0.0.5|502");
    }

    [Fact]
    public void UdpSettings_GetID_FormatsAsExpected()
    {
        // Source: "Udp|{Name}|{Address}|{Port}|{LocalPort}"
        new UdpSettings { Name = "beacon", Address = "10.0.0.9", Port = 1234, LocalPort = 4321 }
            .GetID().Should().Be("Udp|beacon|10.0.0.9|1234|4321");
    }

    // ---- Read(size): non-destructive, negative = all, oversize = empty ----

    [Fact]
    public void Tcp_Read_ReturnsPrefix_NonDestructive()
    {
        var port = NewTcp(1, 2, 3, 4);
        port.Read(2).Should().Equal(1, 2);
        port.ReadData.Should().Equal(1, 2, 3, 4); // unchanged
    }

    [Fact]
    public void Udp_Read_ReturnsPrefix_NonDestructive()
    {
        var port = NewUdp(1, 2, 3, 4);
        port.Read(2).Should().Equal(1, 2);
        port.ReadData.Should().Equal(1, 2, 3, 4); // unchanged
    }

    [Fact]
    public void Tcp_Read_Negative_ReturnsAll_EmptyBufferReturnsEmpty()
    {
        NewTcp(5, 6, 7).Read(-1).Should().Equal(5, 6, 7);
        NewTcp().Read(-1).Should().BeEmpty();
    }

    [Fact]
    public void Udp_Read_Negative_ReturnsAll_EmptyBufferReturnsEmpty()
    {
        NewUdp(5, 6, 7).Read(-1).Should().Equal(5, 6, 7);
        NewUdp().Read(-1).Should().BeEmpty();
    }

    [Fact]
    public void Tcp_Read_SizeGreaterThanCount_ReturnsEmpty_NoThrow()
    {
        var port = NewTcp(1, 2);
        port.Read(5).Should().BeEmpty();
        port.ReadData.Should().Equal(1, 2); // unchanged
    }

    [Fact]
    public void Udp_Read_SizeGreaterThanCount_ReturnsEmpty_NoThrow()
    {
        var port = NewUdp(1, 2);
        port.Read(5).Should().BeEmpty();
        port.ReadData.Should().Equal(1, 2); // unchanged
    }

    // ---- HasReadData(size) ----

    [Fact]
    public void Tcp_HasReadData_CountThreshold()
    {
        var port = NewTcp(1, 2, 3);
        port.HasReadData(3).Should().BeTrue();
        port.HasReadData(2).Should().BeTrue();
        port.HasReadData(4).Should().BeFalse();
    }

    [Fact]
    public void Udp_HasReadData_CountThreshold()
    {
        var port = NewUdp(1, 2, 3);
        port.HasReadData(3).Should().BeTrue();
        port.HasReadData(2).Should().BeTrue();
        port.HasReadData(4).Should().BeFalse();
    }

    [Fact]
    public void Tcp_HasReadData_Negative_TrueOnlyWhenNonEmpty()
    {
        NewTcp().HasReadData(-1).Should().BeFalse();
        NewTcp(1).HasReadData(-1).Should().BeTrue();
    }

    [Fact]
    public void Udp_HasReadData_Negative_TrueOnlyWhenNonEmpty()
    {
        NewUdp().HasReadData(-1).Should().BeFalse();
        NewUdp(1).HasReadData(-1).Should().BeTrue();
    }

    // ---- RemoveReadData(size): destructive ----

    [Fact]
    public void Tcp_RemoveReadData_Partial_ReturnsAndRemovesPrefix()
    {
        var port = NewTcp(1, 2, 3, 4);
        port.RemoveReadData(2).Should().Equal(1, 2);
        port.ReadData.Should().Equal(3, 4);
    }

    [Fact]
    public void Udp_RemoveReadData_Partial_ReturnsAndRemovesPrefix()
    {
        var port = NewUdp(1, 2, 3, 4);
        port.RemoveReadData(2).Should().Equal(1, 2);
        port.ReadData.Should().Equal(3, 4);
    }

    [Fact]
    public void Tcp_RemoveReadData_Negative_DrainsAll_NoThrow()
    {
        var port = NewTcp(1, 2, 3);
        port.RemoveReadData(-1).Should().Equal(1, 2, 3);
        port.ReadData.Should().BeEmpty();
    }

    [Fact]
    public void Udp_RemoveReadData_Negative_DrainsAll_NoThrow()
    {
        var port = NewUdp(9, 8, 7, 6);
        port.RemoveReadData(-1).Should().Equal(9, 8, 7, 6);
        port.ReadData.Should().BeEmpty();
    }

    [Fact]
    public void Tcp_RemoveReadData_Empty_ReturnsEmpty()
    {
        var port = NewTcp();
        port.RemoveReadData(2).Should().BeEmpty();
        port.RemoveReadData(-1).Should().BeEmpty();
        port.ReadData.Should().BeEmpty();
    }

    [Fact]
    public void Udp_RemoveReadData_Empty_ReturnsEmpty()
    {
        var port = NewUdp();
        port.RemoveReadData(2).Should().BeEmpty();
        port.RemoveReadData(-1).Should().BeEmpty();
        port.ReadData.Should().BeEmpty();
    }

    [Fact]
    public void Tcp_RemoveReadData_SizeGreaterThanCount_RemovesNothing()
    {
        // Read(size>Count) returns empty, so RemoveReadData removes only what a successful Read yields.
        var port = NewTcp(1, 2);
        port.RemoveReadData(5).Should().BeEmpty();
        port.ReadData.Should().Equal(1, 2); // untouched
    }

    [Fact]
    public void Udp_RemoveReadData_SizeGreaterThanCount_RemovesNothing()
    {
        var port = NewUdp(1, 2);
        port.RemoveReadData(5).Should().BeEmpty();
        port.ReadData.Should().Equal(1, 2); // untouched
    }
}
