using FluentAssertions;
using Birko.Communication.IR.Devices;

namespace Birko.Communication.IR.Tests;

public class SamsungAcProfileTests
{
    private readonly SamsungAcProfile _profile = new();

    // ── Codebook / registry ──

    [Fact]
    public void GetCommandNames_ReturnsFullCodebook()
    {
        var names = _profile.GetCommandNames();

        names.Should().HaveCount(29);
        names.Should().Contain(new[]
        {
            "PowerOn", "PowerOff", "PowerToggle",
            "ModeCool", "ModeHeat", "ModeDry", "ModeFan", "ModeAuto",
            "TempUp", "TempDown", "Temp16", "Temp30",
            "FanAuto", "FanTurbo",
            "SwingOff", "SwingBoth",
            "WindFreeOn", "WindFreeOff"
        });
    }

    [Fact]
    public void GetCommandNames_AllResolveToNonNullCommands()
    {
        foreach (var name in _profile.GetCommandNames())
        {
            _profile.GetCommand(name).Should().NotBeNull($"'{name}' is in the registry");
        }
    }

    [Fact]
    public void GetCommand_IsCaseInsensitive()
    {
        // registry uses StringComparer.OrdinalIgnoreCase
        _profile.GetCommand("poweron").Should().NotBeNull();
        _profile.GetCommand("POWERON").Should().NotBeNull();
    }

    [Fact]
    public void GetCommand_UnknownName_ReturnsNull()
    {
        _profile.GetCommand("NoSuchCommand").Should().BeNull();
    }

    [Fact]
    public void GetTiming_UnknownName_ReturnsNull()
    {
        _profile.GetTiming("NoSuchCommand").Should().BeNull();
    }

    [Fact]
    public void ProfileMetadata_IsSamsung()
    {
        _profile.Manufacturer.Should().Be("Samsung");
        _profile.Model.Should().Be("AC (Generic)");
        _profile.Protocol.Should().NotBeNull();
    }

    // ── Frame byte layout ──

    [Fact]
    public void GetCommand_ProducesFourteenByteExtendedFrame()
    {
        var cmd = _profile.GetCommand("PowerOn");

        cmd.Should().NotBeNull();
        cmd!.Protocol.Should().Be("Samsung-AC");
        cmd.Address.Should().Be(0x07);
        cmd.BitCount.Should().Be(112); // 14 bytes * 8
        cmd.ExtendedData.Should().NotBeNull();
        cmd.ExtendedData!.Length.Should().Be(14);

        // Fixed section-1 header (bytes 0-6)
        cmd.ExtendedData[0].Should().Be(0x02);
        cmd.ExtendedData[1].Should().Be(0x92);
        cmd.ExtendedData[2].Should().Be(0x0F);
        cmd.ExtendedData[6].Should().Be(0xF0);
        cmd.ExtendedData[7].Should().Be(0x01);

        // Command mirrors the mode/power byte (data[8])
        cmd.Command.Should().Be(cmd.ExtendedData[8]);
    }

    [Fact]
    public void PowerOn_SetsPowerBit_PowerOff_ClearsIt()
    {
        var on = _profile.GetCommand("PowerOn")!;
        (on.ExtendedData![8] & 0x20).Should().Be(0x20, "PowerOn sets the power bit");

        var off = _profile.GetCommand("PowerOff")!;
        (off.ExtendedData![8] & 0x20).Should().Be(0x00, "PowerOff clears the power bit");
    }

    [Fact]
    public void ModeBits_AreEncodedInLowNibbleOfByte8()
    {
        var cool = _profile.GetCommand("ModeCool")!;
        (cool.ExtendedData![8] & 0x0F).Should().Be((byte)SamsungAcMode.Cool);

        var heat = _profile.GetCommand("ModeHeat")!;
        (heat.ExtendedData![8] & 0x0F).Should().Be((byte)SamsungAcMode.Heat);
    }

    // ── Temperature clamping at the 16/30 bounds ──

    [Fact]
    public void Temp16_EncodesZeroOffsetInByte9()
    {
        var cmd = _profile.GetCommand("Temp16")!;
        (cmd.ExtendedData![9] & 0x0F).Should().Be(0); // 16 - 16
    }

    [Fact]
    public void Temp30_EncodesFourteenOffsetInByte9()
    {
        var cmd = _profile.GetCommand("Temp30")!;
        (cmd.ExtendedData![9] & 0x0F).Should().Be(14); // 30 - 16
    }

    [Fact]
    public void SetTemperature_BelowMin_ClampsTo16()
    {
        _profile.SetTemperature(5);
        var cmd = _profile.GetCommand("ModeCool")!; // rebuilds from current state
        (cmd.ExtendedData![9] & 0x0F).Should().Be(0, "temperature clamps up to 16 °C");
    }

    [Fact]
    public void SetTemperature_AboveMax_ClampsTo30()
    {
        _profile.SetTemperature(45);
        var cmd = _profile.GetCommand("ModeCool")!;
        (cmd.ExtendedData![9] & 0x0F).Should().Be(14, "temperature clamps down to 30 °C");
    }

    [Fact]
    public void TempUp_AtMax_StaysClamped()
    {
        _profile.SetTemperature(30);
        _profile.GetCommand("TempUp"); // 31 -> clamp 30
        var cmd = _profile.GetCommand("ModeCool")!;
        (cmd.ExtendedData![9] & 0x0F).Should().Be(14);
    }

    [Fact]
    public void TempDown_AtMin_StaysClamped()
    {
        _profile.SetTemperature(16);
        _profile.GetCommand("TempDown"); // 15 -> clamp 16
        var cmd = _profile.GetCommand("ModeCool")!;
        (cmd.ExtendedData![9] & 0x0F).Should().Be(0);
    }

    // ── Checksum byte (data[13] = XOR of bytes 7-12) ──

    [Fact]
    public void Checksum_IsXorOfBytes7Through12()
    {
        var cmd = _profile.GetCommand("PowerOn")!;
        var data = cmd.ExtendedData!;

        byte expected = 0;
        for (int i = 7; i < 13; i++)
        {
            expected ^= data[i];
        }

        data[13].Should().Be(expected);
    }

    [Fact]
    public void Checksum_ChangesWhenStateChanges()
    {
        var cool = _profile.GetCommand("ModeCool")!.ExtendedData![13];
        var heat = _profile.GetCommand("ModeHeat")!.ExtendedData![13];

        heat.Should().NotBe(cool, "a different mode alters the XOR checksum");
    }

    // ── Timing / encode ──

    [Fact]
    public void GetTiming_KnownCommand_ProducesThreeSectionFrame()
    {
        var timing = _profile.GetTiming("PowerOn");

        timing.Should().NotBeNull();
        timing!.CarrierFrequencyHz.Should().Be(38000);
        timing.Durations[0].Should().Be(3000);  // leader mark
        timing.Durations[1].Should().Be(8900);  // leader space

        // 2 (leader) + 112 (7 bytes) + 1 (stop) + 2 (section gap) + 112 (7 bytes) + 1 (stop) = 230
        timing.Durations.Should().HaveCount(230);
    }
}
