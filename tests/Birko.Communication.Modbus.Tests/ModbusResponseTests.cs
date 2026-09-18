using System;
using Birko.Communication.Modbus.Protocols;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.Modbus.Tests
{
    public class ModbusResponseTests
    {
        [Fact]
        public void GetRegisters_TwoRegisters_ReturnsCorrectValues()
        {
            var data = new byte[] { 0x00, 0x64, 0x00, 0xC8 }; // 100, 200
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadHoldingRegisters, data);

            var registers = response.GetRegisters();

            registers.Should().HaveCount(2);
            registers[0].Should().Be(100);
            registers[1].Should().Be(200);
        }

        [Fact]
        public void GetRegisters_HighValues_HandlesBigEndian()
        {
            var data = new byte[] { 0xFF, 0xFF, 0x80, 0x00 }; // 65535, 32768
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadHoldingRegisters, data);

            var registers = response.GetRegisters();

            registers[0].Should().Be(65535);
            registers[1].Should().Be(32768);
        }

        [Fact]
        public void GetRegisters_EmptyData_ReturnsEmptyArray()
        {
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadHoldingRegisters, Array.Empty<byte>());
            response.GetRegisters().Should().BeEmpty();
        }

        [Fact]
        public void GetCoils_EightCoils_UnpacksCorrectly()
        {
            // Binary: 10110010 = coils: false, true, false, false, true, true, false, true
            var data = new byte[] { 0b_1011_0010 };
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadCoils, data);

            var coils = response.GetCoils(8);

            coils.Should().HaveCount(8);
            coils[0].Should().BeFalse(); // bit 0
            coils[1].Should().BeTrue();  // bit 1
            coils[2].Should().BeFalse(); // bit 2
            coils[3].Should().BeFalse(); // bit 3
            coils[4].Should().BeTrue();  // bit 4
            coils[5].Should().BeTrue();  // bit 5
            coils[6].Should().BeFalse(); // bit 6
            coils[7].Should().BeTrue();  // bit 7
        }

        [Fact]
        public void GetCoils_ThreeCoils_ReturnsPartialByte()
        {
            var data = new byte[] { 0b_0000_0101 }; // bits 0 and 2 set
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadCoils, data);

            var coils = response.GetCoils(3);

            coils.Should().HaveCount(3);
            coils[0].Should().BeTrue();
            coils[1].Should().BeFalse();
            coils[2].Should().BeTrue();
        }

        [Fact]
        public void GetCoils_SixteenCoils_SpansTwoBytes()
        {
            var data = new byte[] { 0xFF, 0x00 }; // first 8 ON, next 8 OFF
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadCoils, data);

            var coils = response.GetCoils(16);

            coils.Should().HaveCount(16);
            for (int i = 0; i < 8; i++)
                coils[i].Should().BeTrue($"coil {i} should be ON");
            for (int i = 8; i < 16; i++)
                coils[i].Should().BeFalse($"coil {i} should be OFF");
        }

        [Fact]
        public void GetCoils_ZeroQuantity_ReturnsEmpty()
        {
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadCoils, new byte[] { 0xFF });
            response.GetCoils(0).Should().BeEmpty();
        }

        [Fact]
        public void GetFloat32_ValidData_ReturnsCorrectFloat()
        {
            // IEEE 754: 42.5f = 0x42_2A_00_00 big-endian
            // Registers: [0x422A, 0x0000] → bytes: 0x42, 0x2A, 0x00, 0x00
            var bytes = BitConverter.GetBytes(42.5f);
            // Convert to big-endian word order (AB CD)
            var data = new byte[] { bytes[3], bytes[2], bytes[1], bytes[0] };
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadHoldingRegisters, data);

            response.GetFloat32().Should().Be(42.5f);
        }

        [Fact]
        public void GetFloat32_WithOffset_ReadsFromCorrectPosition()
        {
            // 2 registers padding + float at offset 2
            var bytes = BitConverter.GetBytes(100.0f);
            var data = new byte[] { 0x00, 0x00, 0x00, 0x00, bytes[3], bytes[2], bytes[1], bytes[0] };
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadHoldingRegisters, data);

            response.GetFloat32(registerOffset: 2).Should().Be(100.0f);
        }

        [Fact]
        public void GetFloat32_InsufficientData_ThrowsInvalidOperation()
        {
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadHoldingRegisters, new byte[] { 0x00, 0x01 });

            var act = () => response.GetFloat32();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Float32*");
        }

        [Fact]
        public void GetInt32_ValidData_ReturnsCorrectValue()
        {
            // 100000 = 0x000186A0 big-endian
            var data = new byte[] { 0x00, 0x01, 0x86, 0xA0 };
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadHoldingRegisters, data);

            response.GetInt32().Should().Be(100000);
        }

        [Fact]
        public void GetInt32_NegativeValue_ReturnsCorrectValue()
        {
            // -1 = 0xFFFFFFFF big-endian
            var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadHoldingRegisters, data);

            response.GetInt32().Should().Be(-1);
        }

        [Fact]
        public void GetInt32_WithOffset_ReadsFromCorrectPosition()
        {
            var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadHoldingRegisters, data);

            response.GetInt32(registerOffset: 2).Should().Be(1);
        }

        [Fact]
        public void GetInt32_InsufficientData_ThrowsInvalidOperation()
        {
            var response = new ModbusResponse(0, 1, ModbusFunction.ReadHoldingRegisters, new byte[] { 0x00 });

            var act = () => response.GetInt32();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Int32*");
        }

        [Fact]
        public void Constructor_StoresAllProperties()
        {
            var data = new byte[] { 0x01, 0x02 };
            var response = new ModbusResponse(42, 5, ModbusFunction.ReadInputRegisters, data);

            response.TransactionId.Should().Be(42);
            response.UnitId.Should().Be(5);
            response.Function.Should().Be(ModbusFunction.ReadInputRegisters);
            response.Data.Should().BeEquivalentTo(data);
        }
    }
}
