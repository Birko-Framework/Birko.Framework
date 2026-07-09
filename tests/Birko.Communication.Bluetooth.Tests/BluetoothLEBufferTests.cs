using System.Linq;
using Birko.Communication.Bluetooth.Ports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.Bluetooth.Tests;

/// <summary>
/// Regression for CR-H016: Read(-1) means "read all available", but RemoveReadData(-1) used to
/// call ReadData.RemoveRange(0, -1) (throwing ArgumentOutOfRangeException) and HasReadData(-1)
/// returned true even on an empty buffer. These paths are platform-agnostic (buffer only), so they
/// run without any Bluetooth hardware.
/// </summary>
public class BluetoothLEBufferTests
{
    private static BluetoothLE NewPortWith(params byte[] data)
    {
        var port = new BluetoothLE(new BluetoothLESettings { Name = "test" });
        port.ReadData.AddRange(data);
        return port;
    }

    [Fact]
    public void RemoveReadData_All_DrainsBufferWithoutThrowing()
    {
        var port = NewPortWith(1, 2, 3, 4);

        var removed = port.RemoveReadData(-1);

        removed.Should().Equal(1, 2, 3, 4);
        port.ReadData.Should().BeEmpty();
    }

    [Fact]
    public void RemoveReadData_All_OnEmptyBuffer_ReturnsEmpty()
    {
        var port = NewPortWith();

        var removed = port.RemoveReadData(-1);

        removed.Should().BeEmpty();
        port.ReadData.Should().BeEmpty();
    }

    [Fact]
    public void RemoveReadData_Partial_RemovesOnlyThatMany()
    {
        var port = NewPortWith(10, 20, 30, 40);

        var removed = port.RemoveReadData(2);

        removed.Should().Equal(10, 20);
        port.ReadData.Should().Equal(30, 40);
    }

    [Fact]
    public void HasReadData_NegativeSize_IsFalseOnEmptyAndTrueWhenData()
    {
        NewPortWith().HasReadData(-1).Should().BeFalse("empty buffer has nothing to drain");
        NewPortWith(1).HasReadData(-1).Should().BeTrue();
    }

    [Fact]
    public void HasReadData_ExactSize_Boundary()
    {
        var port = NewPortWith(1, 2, 3);

        port.HasReadData(3).Should().BeTrue();
        port.HasReadData(4).Should().BeFalse();
    }

    // --- Read() (CR-M036: the availability check and GetRange are now under one lock) ---

    [Fact]
    public void Read_Partial_ReturnsRequestedPrefix_WithoutRemoving()
    {
        var port = NewPortWith(1, 2, 3, 4);

        port.Read(2).Should().Equal(1, 2);
        port.ReadData.Should().Equal(1, 2, 3, 4); // Read is non-destructive
    }

    [Fact]
    public void Read_All_ReturnsWholeBuffer()
    {
        var port = NewPortWith(5, 6, 7);
        port.Read(-1).Should().Equal(5, 6, 7);
    }

    [Fact]
    public void Read_MoreThanAvailable_ReturnsEmpty_DoesNotThrow()
    {
        var port = NewPortWith(1, 2);

        byte[] result = null!;
        var act = () => result = port.Read(5);

        act.Should().NotThrow();
        result.Should().BeEmpty("an under-filled buffer yields nothing, never a partial/exception");
    }

    [Fact]
    public void Read_NegativeOnEmpty_ReturnsEmpty()
    {
        NewPortWith().Read(-1).Should().BeEmpty();
    }

    // --- GetID formatting (CR-M042) ---

    [Fact]
    public void BluetoothLESettings_GetID_FormatsAllFields()
    {
        var settings = new BluetoothLESettings
        {
            Name = "dev",
            DeviceAddress = "AA:BB:CC:DD:EE:FF"
        };

        settings.GetID().Should().Be("BluetoothLE|dev|AA:BB:CC:DD:EE:FF|none|none");
    }

    [Fact]
    public void BluetoothLESettings_GetID_IncludesUuids_WhenSet()
    {
        var svc = System.Guid.Parse("0000180d-0000-1000-8000-00805f9b34fb");
        var chr = System.Guid.Parse("00002a37-0000-1000-8000-00805f9b34fb");
        var settings = new BluetoothLESettings
        {
            Name = "hrm",
            DeviceAddress = "11:22:33:44:55:66",
            ServiceUuid = svc,
            CharacteristicUuid = chr
        };

        settings.GetID().Should().Be($"BluetoothLE|hrm|11:22:33:44:55:66|{svc}|{chr}");
    }

    [Fact]
    public void BluetoothSettings_GetID_FormatsSerialFields()
    {
        var settings = new BluetoothSettings { Name = "spp" };
        settings.GetID().Should().StartWith("Bluetooth|spp|");
    }
}
