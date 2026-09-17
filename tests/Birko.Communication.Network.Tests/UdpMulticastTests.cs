using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Birko.Communication.Network.Ports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.Network.Tests;

/// <summary>
/// Multicast receive + address reuse on <see cref="Udp"/> (TASK-455).
///
/// ⚠ These bind real sockets on the loopback interface, unlike <see cref="NetworkPortTests"/>, which
/// is deliberately socket-free. They are here because the defect is *entirely* in socket setup:
/// a test that seeded <c>ReadData</c> would pass against the unfixed code.
///
/// ⚠ Every assertion is on BYTES RECEIVED, never on "no exception was thrown". UDP send is
/// fire-and-forget, so a send that throws nothing proves nothing — that mistake was made while
/// measuring this defect and is recorded on the task.
/// </summary>
public class UdpMulticastTests
{
    // Group and ports picked to avoid the real discovery ranges (mDNS 5353, SSDP 1900) so a test run
    // cannot disturb, or be disturbed by, anything actually doing discovery on this machine.
    private const string Group = "239.7.7.7";
    private const int Port = 45455;
    private const int RecvTimeoutMs = 1500;

    private static Udp NewPort(int localPort, string? group = null, bool reuse = false) =>
        new(new UdpSettings
        {
            Name = "mc",
            Address = Group,          // Write() targets the group
            Port = localPort,
            LocalPort = localPort,
            MulticastGroup = group,
            ReuseAddress = reuse,
        });

    /// <summary>Sends one datagram to the group from an independent socket.</summary>
    private static void SendToGroup(int port, params byte[] payload)
    {
        using var tx = new UdpClient(0);
        tx.Send(payload, payload.Length, new IPEndPoint(IPAddress.Parse(Group), port));
    }

    /// <summary>Polls the port's buffer rather than sleeping a fixed time, so the test is not racy.</summary>
    private static bool WaitForBytes(Udp port, int count, int timeoutMs = RecvTimeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (port.HasReadData(count)) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    // ---------- criterion 1: multicast receive works, and the join is what makes it work ----------

    [Fact]
    public void WithMulticastGroup_ReceivesGroupDatagram()
    {
        var port = NewPort(Port, Group);
        try
        {
            port.Open();
            Thread.Sleep(100); // let the read thread reach Receive
            SendToGroup(Port, 9, 9);

            WaitForBytes(port, 2).Should().BeTrue("a joined socket must receive datagrams sent to the group");
            port.RemoveReadData(2).Should().Equal(9, 9);
        }
        finally { port.Close(); }
    }

    [Fact]
    public void WithoutMulticastGroup_DoesNotReceiveGroupDatagram()
    {
        // The control for the test above. Without it, "it received something" could be explained by
        // the datagram arriving for some other reason, and the join would be untested.
        var port = NewPort(Port + 1, group: null);
        try
        {
            port.Open();
            Thread.Sleep(100);
            SendToGroup(Port + 1, 9, 9);

            WaitForBytes(port, 1, 600).Should().BeFalse("group membership is required to receive multicast");
        }
        finally { port.Close(); }
    }

    // ---------- criterion 2: two listeners on one port, both delivered ----------

    [Fact]
    public void TwoPortsWithReuseAddress_BothReceiveTheSameDatagram()
    {
        // This is the case ReuseAddress exists for: the real discovery ports already have the OS
        // resolver bound, so one listener succeeding proves nothing.
        var a = NewPort(Port + 2, Group, reuse: true);
        var b = NewPort(Port + 2, Group, reuse: true);
        try
        {
            a.Open();
            b.Open();
            Thread.Sleep(100);
            SendToGroup(Port + 2, 4, 2);

            WaitForBytes(a, 2).Should().BeTrue("first listener should receive");
            WaitForBytes(b, 2).Should().BeTrue("second listener on the same port should also receive");
            a.RemoveReadData(2).Should().Equal(4, 2);
            b.RemoveReadData(2).Should().Equal(4, 2);
        }
        finally { a.Close(); b.Close(); }
    }

    [Fact]
    public void WithoutReuseAddress_SecondBindOnSamePortThrows()
    {
        // The control for the test above — proves ReuseAddress is load-bearing rather than decorative.
        var a = NewPort(Port + 3, Group);
        var b = NewPort(Port + 3, Group);
        try
        {
            a.Open();
            Action second = () => b.Open();
            second.Should().Throw<SocketException>("an exclusive bind must still refuse a second socket");
        }
        finally { a.Close(); b.Close(); }
    }

    // ---------- criterion 5: close drops membership, and the port can be reopened ----------

    [Fact]
    public void CloseThenReopen_StillReceives()
    {
        var port = NewPort(Port + 4, Group);
        try
        {
            port.Open();
            port.Close();

            port.Open();
            Thread.Sleep(100);
            SendToGroup(Port + 4, 1, 1);

            WaitForBytes(port, 2).Should().BeTrue("a rejoined port must receive again after a close/open cycle");
        }
        finally { port.Close(); }
    }

    [Fact]
    public void Close_IsIdempotent_AfterMulticastJoin()
    {
        var port = NewPort(Port + 5, Group);
        port.Open();
        port.Close();

        Action again = () => port.Close();
        again.Should().NotThrow("dropping a group twice must not prevent the port from closing");
    }

    // ---------- criterion 3: unchanged when the new fields are unset ----------

    [Fact]
    public void PlainUnicast_StillRoundTrips_WhenNewFieldsUnset()
    {
        // The defaults must remain an exclusive bind with no join — the whole point of adding the
        // fields opt-in rather than changing Open()'s behaviour for everyone.
        const int local = Port + 6;
        var port = new Udp(new UdpSettings
        {
            Name = "unicast", Address = "127.0.0.1", Port = local, LocalPort = local,
        });
        try
        {
            port.Open();
            Thread.Sleep(100);
            using (var tx = new UdpClient(0))
            {
                tx.Send(new byte[] { 3, 3, 3 }, 3, new IPEndPoint(IPAddress.Loopback, local));
            }

            WaitForBytes(port, 3).Should().BeTrue("ordinary unicast receive must be unaffected");
            port.RemoveReadData(3).Should().Equal(3, 3, 3);
        }
        finally { port.Close(); }
    }

    // ---------- criterion 4: GetID ----------

    [Fact]
    public void GetID_IsByteIdenticalWhenNewFieldsUnset()
    {
        // Pins the compatibility promise: adding the fields must not change the id of settings that
        // do not use them.
        new UdpSettings { Name = "beacon", Address = "10.0.0.9", Port = 1234, LocalPort = 4321 }
            .GetID().Should().Be("Udp|beacon|10.0.0.9|1234|4321");
    }

    [Fact]
    public void GetID_DistinguishesEachSocketAffectingField()
    {
        var base_ = new UdpSettings { Name = "n", Address = "1.2.3.4", Port = 1, LocalPort = 2 };
        var mc = new UdpSettings { Name = "n", Address = "1.2.3.4", Port = 1, LocalPort = 2, MulticastGroup = Group };
        var reuse = new UdpSettings { Name = "n", Address = "1.2.3.4", Port = 1, LocalPort = 2, ReuseAddress = true };
        var ttl = new UdpSettings { Name = "n", Address = "1.2.3.4", Port = 1, LocalPort = 2, MulticastTtl = 4 };

        new[] { base_.GetID(), mc.GetID(), reuse.GetID(), ttl.GetID() }
            .Should().OnlyHaveUniqueItems("two settings that bind differently must not share an id");
    }
}
