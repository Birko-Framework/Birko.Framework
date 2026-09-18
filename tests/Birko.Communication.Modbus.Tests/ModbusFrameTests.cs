using System;
using Birko.Communication.Modbus.Protocols;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.Modbus.Tests
{
    public class ModbusFrameTests
    {
        // ── CRC-16 ──

        [Fact]
        public void CalculateCrc16_KnownVector_ReturnsExpectedCrc()
        {
            // Modbus RTU request: unit=1, func=0x03, addr=0x006B, qty=0x0003
            var data = new byte[] { 0x01, 0x03, 0x00, 0x6B, 0x00, 0x03 };
            var crc = ModbusFrame.CalculateCrc16(data, data.Length);

            // Known CRC for this frame
            crc.Should().NotBe(0);
        }

        [Fact]
        public void CalculateCrc16_EmptyData_Returns0xFFFF()
        {
            var crc = ModbusFrame.CalculateCrc16(Array.Empty<byte>(), 0);
            crc.Should().Be(0xFFFF);
        }

        [Fact]
        public void CalculateCrc16_SingleByte_ProducesConsistentResult()
        {
            var data = new byte[] { 0x01 };
            var crc1 = ModbusFrame.CalculateCrc16(data, 1);
            var crc2 = ModbusFrame.CalculateCrc16(data, 1);
            crc1.Should().Be(crc2);
        }

        // ── TCP Request Building ──

        [Fact]
        public void BuildTcpRequest_ReadHoldingRegisters_ProducesCorrectFrame()
        {
            var frame = ModbusFrame.BuildTcpRequest(
                transactionId: 0x0001,
                unitId: 1,
                function: ModbusFunction.ReadHoldingRegisters,
                startAddress: 0x0000,
                quantity: 10);

            // MBAP header (7) + PDU (5) = 12 bytes
            frame.Should().HaveCount(12);

            // Transaction ID
            frame[0].Should().Be(0x00);
            frame[1].Should().Be(0x01);

            // Protocol ID (Modbus = 0)
            frame[2].Should().Be(0x00);
            frame[3].Should().Be(0x00);

            // Length = PDU(5) + UnitId(1) = 6
            frame[4].Should().Be(0x00);
            frame[5].Should().Be(0x06);

            // Unit ID
            frame[6].Should().Be(0x01);

            // Function code
            frame[7].Should().Be(0x03);

            // Start address
            frame[8].Should().Be(0x00);
            frame[9].Should().Be(0x00);

            // Quantity
            frame[10].Should().Be(0x00);
            frame[11].Should().Be(0x0A);
        }

        [Fact]
        public void BuildTcpRequest_ReadCoils_HasCorrectFunctionCode()
        {
            var frame = ModbusFrame.BuildTcpRequest(0, 1, ModbusFunction.ReadCoils, 0, 8);
            frame[7].Should().Be(0x01);
        }

        [Fact]
        public void BuildTcpRequest_ReadDiscreteInputs_HasCorrectFunctionCode()
        {
            var frame = ModbusFrame.BuildTcpRequest(0, 1, ModbusFunction.ReadDiscreteInputs, 0, 8);
            frame[7].Should().Be(0x02);
        }

        [Fact]
        public void BuildTcpRequest_ReadInputRegisters_HasCorrectFunctionCode()
        {
            var frame = ModbusFrame.BuildTcpRequest(0, 1, ModbusFunction.ReadInputRegisters, 0, 1);
            frame[7].Should().Be(0x04);
        }

        // ── TCP Write Request Building ──

        [Fact]
        public void BuildTcpWriteRequest_WrapsRawPdu_WithMbapHeader()
        {
            var pdu = new byte[] { 0x06, 0x00, 0x01, 0x00, 0x03 }; // WriteSingleRegister, addr=1, value=3
            var frame = ModbusFrame.BuildTcpWriteRequest(transactionId: 5, unitId: 2, pdu);

            frame.Should().HaveCount(12); // 7 MBAP + 5 PDU

            // Transaction ID = 5
            frame[0].Should().Be(0x00);
            frame[1].Should().Be(0x05);

            // Unit ID = 2
            frame[6].Should().Be(0x02);

            // PDU starts at [7]
            frame[7].Should().Be(0x06); // function
            frame[8].Should().Be(0x00);
            frame[9].Should().Be(0x01); // address
        }

        // ── TCP Response Parsing ──

        [Fact]
        public void ParseTcpResponse_ReadRegisters_ReturnsCorrectData()
        {
            // Response: MBAP(7) + func(1) + byteCount(1) + data(4) = 13 bytes
            // 2 registers: 0x0064 and 0x00C8
            var response = new byte[]
            {
                0x00, 0x01, // Transaction ID
                0x00, 0x00, // Protocol ID
                0x00, 0x07, // Length
                0x01,       // Unit ID
                0x03,       // Function (ReadHoldingRegisters)
                0x04,       // Byte count
                0x00, 0x64, // Register 0 = 100
                0x00, 0xC8  // Register 1 = 200
            };

            var result = ModbusFrame.ParseTcpResponse(response);

            result.TransactionId.Should().Be(1);
            result.UnitId.Should().Be(1);
            result.Function.Should().Be(ModbusFunction.ReadHoldingRegisters);
            result.Data.Should().HaveCount(4);
        }

        [Fact]
        public void ParseTcpResponse_WriteSingleRegister_ReturnsEchoData()
        {
            // Write single register response: MBAP(7) + func(1) + addr(2) + value(2) = 12 bytes
            var response = new byte[]
            {
                0x00, 0x02, // Transaction ID
                0x00, 0x00, // Protocol ID
                0x00, 0x06, // Length
                0x01,       // Unit ID
                0x06,       // Function (WriteSingleRegister)
                0x00, 0x01, // Address
                0x00, 0x03  // Value
            };

            var result = ModbusFrame.ParseTcpResponse(response);

            result.TransactionId.Should().Be(2);
            result.Function.Should().Be(ModbusFunction.WriteSingleRegister);
            result.Data.Should().HaveCount(4);
            // Echo: addr high, addr low, value high, value low
            result.Data[0].Should().Be(0x00);
            result.Data[1].Should().Be(0x01);
            result.Data[2].Should().Be(0x00);
            result.Data[3].Should().Be(0x03);
        }

        [Fact]
        public void ParseTcpResponse_ErrorResponse_ThrowsModbusException()
        {
            var response = new byte[]
            {
                0x00, 0x01, // Transaction ID
                0x00, 0x00, // Protocol ID
                0x00, 0x03, // Length
                0x01,       // Unit ID
                0x83,       // Error: ReadHoldingRegisters (0x03 | 0x80)
                0x02        // Exception code: Illegal data address
            };

            var act = () => ModbusFrame.ParseTcpResponse(response);
            act.Should().Throw<ModbusException>()
                .Where(e => e.ExceptionCode == 2);
        }

        [Fact]
        public void ParseTcpResponse_TooShort_ThrowsModbusException()
        {
            var act = () => ModbusFrame.ParseTcpResponse(new byte[] { 0x00, 0x01 });
            act.Should().Throw<ModbusException>();
        }

        // ── RTU Request Building ──

        [Fact]
        public void BuildRtuRequest_ReadHoldingRegisters_ProducesCorrectFrame()
        {
            var frame = ModbusFrame.BuildRtuRequest(
                unitId: 1,
                function: ModbusFunction.ReadHoldingRegisters,
                startAddress: 0x006B,
                quantity: 3);

            // unit(1) + PDU(5) + CRC(2) = 8 bytes
            frame.Should().HaveCount(8);
            frame[0].Should().Be(0x01);  // Unit ID
            frame[1].Should().Be(0x03);  // Function
            frame[2].Should().Be(0x00);  // Start address high
            frame[3].Should().Be(0x6B);  // Start address low
            frame[4].Should().Be(0x00);  // Quantity high
            frame[5].Should().Be(0x03);  // Quantity low
        }

        [Fact]
        public void BuildRtuRequest_CrcIsValid()
        {
            var frame = ModbusFrame.BuildRtuRequest(1, ModbusFunction.ReadHoldingRegisters, 0, 1);

            // Verify CRC matches recalculation
            var calculatedCrc = ModbusFrame.CalculateCrc16(frame, frame.Length - 2);
            var frameCrc = (ushort)(frame[^2] | (frame[^1] << 8));
            frameCrc.Should().Be(calculatedCrc);
        }

        // ── RTU Write Request ──

        [Fact]
        public void BuildRtuWriteRequest_WrapsRawPdu_WithCrc()
        {
            var pdu = new byte[] { 0x05, 0x00, 0x00, 0xFF, 0x00 }; // WriteSingleCoil ON
            var frame = ModbusFrame.BuildRtuWriteRequest(unitId: 1, pdu);

            frame.Should().HaveCount(8); // unit(1) + PDU(5) + CRC(2)
            frame[0].Should().Be(0x01); // Unit ID
            frame[1].Should().Be(0x05); // Function

            // Verify CRC
            var calculatedCrc = ModbusFrame.CalculateCrc16(frame, frame.Length - 2);
            var frameCrc = (ushort)(frame[^2] | (frame[^1] << 8));
            frameCrc.Should().Be(calculatedCrc);
        }

        // ── RTU Response Parsing ──

        [Fact]
        public void ParseRtuResponse_ReadRegisters_ReturnsCorrectData()
        {
            // Build a valid RTU response: unit=1, func=0x03, byteCount=2, data=0x01F4, + CRC
            var payload = new byte[] { 0x01, 0x03, 0x02, 0x01, 0xF4 };
            var crc = ModbusFrame.CalculateCrc16(payload, payload.Length);
            var response = new byte[payload.Length + 2];
            Array.Copy(payload, response, payload.Length);
            response[^2] = (byte)(crc & 0xFF);
            response[^1] = (byte)((crc >> 8) & 0xFF);

            var result = ModbusFrame.ParseRtuResponse(response);

            result.UnitId.Should().Be(1);
            result.Function.Should().Be(ModbusFunction.ReadHoldingRegisters);
            result.Data.Should().HaveCount(2);
            result.Data[0].Should().Be(0x01);
            result.Data[1].Should().Be(0xF4);
        }

        [Fact]
        public void ParseRtuResponse_WriteSingleRegister_ReturnsEchoData()
        {
            // Write single register RTU response: unit=1, func=0x06, addr=0x0001, value=0x0003
            var payload = new byte[] { 0x01, 0x06, 0x00, 0x01, 0x00, 0x03 };
            var crc = ModbusFrame.CalculateCrc16(payload, payload.Length);
            var response = new byte[payload.Length + 2];
            Array.Copy(payload, response, payload.Length);
            response[^2] = (byte)(crc & 0xFF);
            response[^1] = (byte)((crc >> 8) & 0xFF);

            var result = ModbusFrame.ParseRtuResponse(response);

            result.Function.Should().Be(ModbusFunction.WriteSingleRegister);
            result.Data.Should().HaveCount(4);
        }

        [Fact]
        public void ParseRtuResponse_CrcMismatch_ThrowsInvalidOperation()
        {
            var response = new byte[] { 0x01, 0x03, 0x02, 0x01, 0xF4, 0x00, 0x00 }; // bad CRC

            var act = () => ModbusFrame.ParseRtuResponse(response);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*CRC*");
        }

        [Fact]
        public void ParseRtuResponse_ErrorResponse_ThrowsModbusException()
        {
            // Error response: unit=1, func=0x83 (error), exception=3
            var payload = new byte[] { 0x01, 0x83, 0x03 };
            var crc = ModbusFrame.CalculateCrc16(payload, payload.Length);
            var response = new byte[payload.Length + 2];
            Array.Copy(payload, response, payload.Length);
            response[^2] = (byte)(crc & 0xFF);
            response[^1] = (byte)((crc >> 8) & 0xFF);

            var act = () => ModbusFrame.ParseRtuResponse(response);
            act.Should().Throw<ModbusException>()
                .Where(e => e.ExceptionCode == 3);
        }

        [Fact]
        public void ParseRtuResponse_TooShort_ThrowsModbusException()
        {
            var act = () => ModbusFrame.ParseRtuResponse(new byte[] { 0x01, 0x03 });
            act.Should().Throw<ModbusException>();
        }

        // ── PDU Builders ──

        [Fact]
        public void BuildWriteSinglePdu_WriteSingleCoil_ProducesCorrectPdu()
        {
            var pdu = ModbusFrame.BuildWriteSinglePdu(ModbusFunction.WriteSingleCoil, 0x0005, 0xFF00);

            pdu.Should().HaveCount(5);
            pdu[0].Should().Be(0x05); // function
            pdu[1].Should().Be(0x00); // address high
            pdu[2].Should().Be(0x05); // address low
            pdu[3].Should().Be(0xFF); // value high (ON)
            pdu[4].Should().Be(0x00); // value low
        }

        [Fact]
        public void BuildWriteSinglePdu_WriteSingleRegister_ProducesCorrectPdu()
        {
            var pdu = ModbusFrame.BuildWriteSinglePdu(ModbusFunction.WriteSingleRegister, 0x0001, 0x1234);

            pdu.Should().HaveCount(5);
            pdu[0].Should().Be(0x06);
            pdu[3].Should().Be(0x12);
            pdu[4].Should().Be(0x34);
        }

        [Fact]
        public void BuildWriteMultiplePdu_WriteMultipleRegisters_ProducesCorrectPdu()
        {
            var data = new byte[] { 0x00, 0x0A, 0x01, 0x02 }; // 2 registers
            var pdu = ModbusFrame.BuildWriteMultiplePdu(
                ModbusFunction.WriteMultipleRegisters, 0x0001, 2, data);

            pdu.Should().HaveCount(10); // func(1) + addr(2) + qty(2) + byteCount(1) + data(4)
            pdu[0].Should().Be(0x10); // function
            pdu[1].Should().Be(0x00); // start address high
            pdu[2].Should().Be(0x01); // start address low
            pdu[3].Should().Be(0x00); // quantity high
            pdu[4].Should().Be(0x02); // quantity low
            pdu[5].Should().Be(0x04); // byte count
            pdu[6].Should().Be(0x00); // data[0]
            pdu[7].Should().Be(0x0A); // data[1]
        }
    }
}
