using System;
using Birko.Communication.Modbus.Protocols;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.Modbus.Tests
{
    public class ModbusClientTests
    {
        // ── Constructor ──

        [Fact]
        public void Constructor_NullPort_ThrowsArgumentNullException()
        {
            var act = () => new ModbusClient(null!, ModbusTransport.Tcp);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_SetsDefaultTimeouts()
        {
            var port = new MockPort();
            using var client = new ModbusClient(port, ModbusTransport.Tcp);

            client.ResponseTimeoutMs.Should().Be(3000);
            client.PollIntervalMs.Should().Be(10);
        }

        // ── Connect / Disconnect ──

        [Fact]
        public void Connect_PortClosed_OpensPort()
        {
            var port = new MockPort();
            using var client = new ModbusClient(port, ModbusTransport.Tcp);

            client.Connect();

            port.IsOpen().Should().BeTrue();
            port.OpenCount.Should().Be(1);
        }

        [Fact]
        public void Connect_PortAlreadyOpen_DoesNotOpenAgain()
        {
            var port = new MockPort();
            port.Open();
            using var client = new ModbusClient(port, ModbusTransport.Tcp);

            client.Connect();

            port.OpenCount.Should().Be(1); // Only the initial open
        }

        [Fact]
        public void Disconnect_PortOpen_ClosesPort()
        {
            var port = new MockPort();
            port.Open();
            using var client = new ModbusClient(port, ModbusTransport.Tcp);

            client.Disconnect();

            port.IsOpen().Should().BeFalse();
            port.CloseCount.Should().Be(1);
        }

        [Fact]
        public void Disconnect_PortAlreadyClosed_DoesNotClose()
        {
            var port = new MockPort();
            using var client = new ModbusClient(port, ModbusTransport.Tcp);

            client.Disconnect();

            port.CloseCount.Should().Be(0);
        }

        [Fact]
        public void Dispose_ClosesPort()
        {
            var port = new MockPort();
            port.Open();
            var client = new ModbusClient(port, ModbusTransport.Tcp);

            client.Dispose();

            port.IsOpen().Should().BeFalse();
        }

        // ── TCP Read Operations ──

        [Fact]
        public void ReadHoldingRegisters_Tcp_SendsCorrectRequestAndParsesResponse()
        {
            var port = new MockPort();
            // Pre-load TCP response: 2 registers (0x0064, 0x00C8)
            port.ResponseData = new byte[]
            {
                0x00, 0x00, // Transaction ID
                0x00, 0x00, // Protocol ID
                0x00, 0x07, // Length
                0x01,       // Unit ID
                0x03,       // Function
                0x04,       // Byte count
                0x00, 0x64, // Register 0 = 100
                0x00, 0xC8  // Register 1 = 200
            };

            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            var response = client.ReadHoldingRegisters(unitId: 1, startAddress: 0, quantity: 2);

            response.Function.Should().Be(ModbusFunction.ReadHoldingRegisters);
            var registers = response.GetRegisters();
            registers.Should().HaveCount(2);
            registers[0].Should().Be(100);
            registers[1].Should().Be(200);

            // Verify request was sent
            port.WrittenData.Should().HaveCount(1);
            port.WrittenData[0][7].Should().Be(0x03); // Function code in request
        }

        [Fact]
        public void ReadInputRegisters_Tcp_SendsCorrectFunctionCode()
        {
            var port = new MockPort();
            port.ResponseData = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x05,
                0x01, 0x04, 0x02, 0x01, 0xF4
            };

            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            var response = client.ReadInputRegisters(1, 0, 1);

            response.Function.Should().Be(ModbusFunction.ReadInputRegisters);
            port.WrittenData[0][7].Should().Be(0x04);
        }

        [Fact]
        public void ReadCoils_Tcp_SendsCorrectRequest()
        {
            var port = new MockPort();
            // 8 coils = 1 byte of data: 0b10101010
            port.ResponseData = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x04,
                0x01, 0x01, 0x01, 0xAA
            };

            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            var response = client.ReadCoils(1, 0, 8);

            response.Function.Should().Be(ModbusFunction.ReadCoils);
            var coils = response.GetCoils(8);
            coils.Should().HaveCount(8);
            port.WrittenData[0][7].Should().Be(0x01);
        }

        [Fact]
        public void ReadDiscreteInputs_Tcp_SendsCorrectRequest()
        {
            var port = new MockPort();
            port.ResponseData = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x04,
                0x01, 0x02, 0x01, 0xFF
            };

            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            var response = client.ReadDiscreteInputs(1, 0, 8);

            response.Function.Should().Be(ModbusFunction.ReadDiscreteInputs);
            port.WrittenData[0][7].Should().Be(0x02);
        }

        // ── TCP Write Operations ──

        [Fact]
        public void WriteSingleCoil_Tcp_SendsCorrectRequest()
        {
            var port = new MockPort();
            port.ResponseData = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x06,
                0x01, 0x05, 0x00, 0x00, 0xFF, 0x00
            };

            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            var response = client.WriteSingleCoil(1, 0, true);

            response.Function.Should().Be(ModbusFunction.WriteSingleCoil);
            // Check request: function=0x05, value=0xFF00 (ON)
            var request = port.WrittenData[0];
            request[7].Should().Be(0x05);
            request[10].Should().Be(0xFF);
            request[11].Should().Be(0x00);
        }

        [Fact]
        public void WriteSingleCoil_FalseValue_Sends0x0000()
        {
            var port = new MockPort();
            port.ResponseData = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x06,
                0x01, 0x05, 0x00, 0x00, 0x00, 0x00
            };

            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            client.WriteSingleCoil(1, 0, false);

            var request = port.WrittenData[0];
            request[10].Should().Be(0x00);
            request[11].Should().Be(0x00);
        }

        [Fact]
        public void WriteSingleRegister_Tcp_SendsCorrectRequest()
        {
            var port = new MockPort();
            port.ResponseData = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x06,
                0x01, 0x06, 0x00, 0x01, 0x12, 0x34
            };

            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            var response = client.WriteSingleRegister(1, 1, 0x1234);

            response.Function.Should().Be(ModbusFunction.WriteSingleRegister);
            var request = port.WrittenData[0];
            request[7].Should().Be(0x06);
            request[10].Should().Be(0x12);
            request[11].Should().Be(0x34);
        }

        [Fact]
        public void WriteMultipleRegisters_Tcp_SendsCorrectRequest()
        {
            var port = new MockPort();
            port.ResponseData = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x06,
                0x01, 0x10, 0x00, 0x00, 0x00, 0x02
            };

            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            var response = client.WriteMultipleRegisters(1, 0, new ushort[] { 0x000A, 0x000B });

            response.Function.Should().Be(ModbusFunction.WriteMultipleRegisters);
            var request = port.WrittenData[0];
            request[7].Should().Be(0x10); // function
            // Verify register data in request PDU
            // MBAP(7) + func(1) + addr(2) + qty(2) + byteCount(1) + data
            request[12].Should().Be(0x04); // byte count = 4
            request[13].Should().Be(0x00); // register 0 high
            request[14].Should().Be(0x0A); // register 0 low
            request[15].Should().Be(0x00); // register 1 high
            request[16].Should().Be(0x0B); // register 1 low
        }

        [Fact]
        public void WriteMultipleCoils_Tcp_PacksBitsCorrectly()
        {
            var port = new MockPort();
            port.ResponseData = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x06,
                0x01, 0x0F, 0x00, 0x00, 0x00, 0x04
            };

            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            client.WriteMultipleCoils(1, 0, new bool[] { true, false, true, true });

            var request = port.WrittenData[0];
            request[7].Should().Be(0x0F); // function
            // MBAP(7) + func(1) + addr(2) + qty(2) + byteCount(1) + data(1)
            request[12].Should().Be(0x01); // byte count = 1
            request[13].Should().Be(0b_0000_1101); // bits: 1,0,1,1 → 0x0D
        }

        // ── RTU Read Operations ──

        [Fact]
        public void ReadHoldingRegisters_Rtu_SendsCorrectRequest()
        {
            var port = new MockPort();
            // Build valid RTU response
            var payload = new byte[] { 0x01, 0x03, 0x02, 0x00, 0x64 };
            var crc = ModbusFrame.CalculateCrc16(payload, payload.Length);
            var response = new byte[payload.Length + 2];
            Array.Copy(payload, response, payload.Length);
            response[^2] = (byte)(crc & 0xFF);
            response[^1] = (byte)((crc >> 8) & 0xFF);
            port.ResponseData = response;

            using var client = new ModbusClient(port, ModbusTransport.Rtu);
            var result = client.ReadHoldingRegisters(1, 0, 1);

            result.Function.Should().Be(ModbusFunction.ReadHoldingRegisters);
            var registers = result.GetRegisters();
            registers[0].Should().Be(100);

            // Verify RTU request has CRC
            var request = port.WrittenData[0];
            request.Should().HaveCount(8); // unit(1) + PDU(5) + CRC(2)
        }

        // ── Timeout ──

        [Fact]
        public void ReadHoldingRegisters_NoResponse_ThrowsTimeoutException()
        {
            var port = new MockPort();
            // No response data set — port buffer stays empty

            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            client.ResponseTimeoutMs = 50; // Short timeout for test
            client.PollIntervalMs = 10;

            var act = () => client.ReadHoldingRegisters(1, 0, 1);
            act.Should().Throw<TimeoutException>()
                .WithMessage("*timeout*");
        }

        // ── Auto-connect ──

        [Fact]
        public void ReadHoldingRegisters_AutoConnects_WhenPortClosed()
        {
            var port = new MockPort();
            port.ResponseData = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x05,
                0x01, 0x03, 0x02, 0x00, 0x01
            };

            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            // Don't call Connect() — should auto-connect
            client.ReadHoldingRegisters(1, 0, 1);

            port.OpenCount.Should().Be(1);
        }

        // ── Port interaction ──

        [Fact]
        public void ReadHoldingRegisters_ClearsPortBuffer_BeforeRequest()
        {
            var port = new MockPort();
            port.ResponseData = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x05,
                0x01, 0x03, 0x02, 0x00, 0x01
            };

            using var client = new ModbusClient(port, ModbusTransport.Tcp);
            client.ReadHoldingRegisters(1, 0, 1);

            port.ClearCount.Should().Be(1);
        }

        // ── Transaction ID ──

        [Fact]
        public void ReadHoldingRegisters_Tcp_IncrementsTransactionId()
        {
            var port = new MockPort();
            var responseTemplate = new byte[]
            {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x05,
                0x01, 0x03, 0x02, 0x00, 0x01
            };

            using var client = new ModbusClient(port, ModbusTransport.Tcp);

            port.ResponseData = (byte[])responseTemplate.Clone();
            client.ReadHoldingRegisters(1, 0, 1);
            var firstTxId = (ushort)((port.WrittenData[0][0] << 8) | port.WrittenData[0][1]);

            port.ResponseData = (byte[])responseTemplate.Clone();
            client.ReadHoldingRegisters(1, 0, 1);
            var secondTxId = (ushort)((port.WrittenData[1][0] << 8) | port.WrittenData[1][1]);

            secondTxId.Should().Be((ushort)(firstTxId + 1));
        }
    }
}
