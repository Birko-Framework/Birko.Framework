using System;
using System.IO.Ports;
using Birko.Communication.Hardware.Ports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.Hardware.Tests;

/// <summary>
/// CR-H022 / CR-H023. LPT and Serial both need real hardware/drivers to exercise I/O, but the
/// disposal contract and the stateless RemoveReadData path are verifiable offline.
/// </summary>
public class SerialAndLptTests
{
    private static SerialSettings SerialSettings() => new()
    {
        Name = "COM1",
        BaudRate = 9600,
        Parity = Parity.None,
        DataBits = 8,
        StopBits = StopBits.One
    };

    [Fact]
    public void Serial_IsDisposable()
    {
        typeof(Serial).Should().BeAssignableTo<IDisposable>("CR-H023: Serial owns an IDisposable SerialPort");
    }

    [Fact]
    public void Serial_Dispose_DoesNotThrow_AndIsIdempotent()
    {
        var serial = new Serial(SerialSettings());

        var dispose = () => serial.Dispose();

        dispose.Should().NotThrow();
        dispose.Should().NotThrow("Dispose must be idempotent");
        serial.IsOpen().Should().BeFalse();
    }

    [Fact]
    public void Lpt_RemoveReadData_Zero_ReturnsEmpty_WithoutTouchingBuffer()
    {
        // CR-H022: RemoveReadData no longer calls ReadData.RemoveRange (which threw for size > 0 on
        // the always-empty buffer). size 0 needs no driver P/Invoke, so it runs offline.
        var lpt = new LPT(new LPTSettings { Name = "LPT1", Number = 1 });

        var result = lpt.RemoveReadData(0);

        result.Should().BeEmpty();
        lpt.ReadData.Should().BeEmpty();
    }
}
