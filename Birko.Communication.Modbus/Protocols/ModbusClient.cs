using System;
using System.Threading;
using Birko.Communication.Ports;

namespace Birko.Communication.Modbus.Protocols
{
    /// <summary>
    /// Modbus master client over any IPort (TCP via TcpIp, RTU via Serial).
    /// Thread-safe — one request at a time via lock.
    /// </summary>
    public class ModbusClient : IDisposable
    {
        private readonly IPort _port;
        private readonly ModbusTransport _transport;
        private readonly object _lock = new();
        private ushort _transactionId;

        /// <summary>
        /// Timeout in milliseconds to wait for a response.
        /// </summary>
        public int ResponseTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// Polling interval in ms when waiting for response data in the port buffer.
        /// </summary>
        public int PollIntervalMs { get; set; } = 10;

        public ModbusClient(IPort port, ModbusTransport transport)
        {
            _port = port ?? throw new ArgumentNullException(nameof(port));
            _transport = transport;
        }

        /// <summary>
        /// Opens the underlying port if not already open.
        /// </summary>
        public void Connect()
        {
            if (!_port.IsOpen())
                _port.Open();
        }

        /// <summary>
        /// Closes the underlying port.
        /// </summary>
        public void Disconnect()
        {
            if (_port.IsOpen())
                _port.Close();
        }

        // ── Read Operations ──

        /// <summary>
        /// Reads coils (function 0x01).
        /// </summary>
        public ModbusResponse ReadCoils(byte unitId, ushort startAddress, ushort quantity)
        {
            return SendReadRequest(unitId, ModbusFunction.ReadCoils, startAddress, quantity);
        }

        /// <summary>
        /// Reads discrete inputs (function 0x02).
        /// </summary>
        public ModbusResponse ReadDiscreteInputs(byte unitId, ushort startAddress, ushort quantity)
        {
            return SendReadRequest(unitId, ModbusFunction.ReadDiscreteInputs, startAddress, quantity);
        }

        /// <summary>
        /// Reads holding registers (function 0x03).
        /// </summary>
        public ModbusResponse ReadHoldingRegisters(byte unitId, ushort startAddress, ushort quantity)
        {
            return SendReadRequest(unitId, ModbusFunction.ReadHoldingRegisters, startAddress, quantity);
        }

        /// <summary>
        /// Reads input registers (function 0x04).
        /// </summary>
        public ModbusResponse ReadInputRegisters(byte unitId, ushort startAddress, ushort quantity)
        {
            return SendReadRequest(unitId, ModbusFunction.ReadInputRegisters, startAddress, quantity);
        }

        // ── Write Operations ──

        /// <summary>
        /// Writes a single coil (function 0x05).
        /// </summary>
        public ModbusResponse WriteSingleCoil(byte unitId, ushort address, bool value)
        {
            ushort coilValue = value ? (ushort)0xFF00 : (ushort)0x0000;
            var pdu = ModbusFrame.BuildWriteSinglePdu(ModbusFunction.WriteSingleCoil, address, coilValue);
            return SendWriteRequest(unitId, pdu, 8); // response: func(1) + addr(2) + value(2) = 5 PDU bytes
        }

        /// <summary>
        /// Writes a single register (function 0x06).
        /// </summary>
        public ModbusResponse WriteSingleRegister(byte unitId, ushort address, ushort value)
        {
            var pdu = ModbusFrame.BuildWriteSinglePdu(ModbusFunction.WriteSingleRegister, address, value);
            return SendWriteRequest(unitId, pdu, 8);
        }

        /// <summary>
        /// Writes multiple coils (function 0x0F).
        /// </summary>
        public ModbusResponse WriteMultipleCoils(byte unitId, ushort startAddress, bool[] values)
        {
            var byteCount = (values.Length + 7) / 8;
            var data = new byte[byteCount];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                    data[i / 8] |= (byte)(1 << (i % 8));
            }

            var pdu = ModbusFrame.BuildWriteMultiplePdu(ModbusFunction.WriteMultipleCoils, startAddress, (ushort)values.Length, data);
            return SendWriteRequest(unitId, pdu, 8); // response: func(1) + addr(2) + quantity(2) = 5 PDU bytes
        }

        /// <summary>
        /// Writes multiple registers (function 0x10).
        /// </summary>
        public ModbusResponse WriteMultipleRegisters(byte unitId, ushort startAddress, ushort[] values)
        {
            var data = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                data[i * 2] = (byte)(values[i] >> 8);
                data[i * 2 + 1] = (byte)(values[i] & 0xFF);
            }

            var pdu = ModbusFrame.BuildWriteMultiplePdu(ModbusFunction.WriteMultipleRegisters, startAddress, (ushort)values.Length, data);
            return SendWriteRequest(unitId, pdu, 8);
        }

        // ── Internal ──

        private ModbusResponse SendReadRequest(byte unitId, ModbusFunction function, ushort startAddress, ushort quantity)
        {
            // Calculate expected response size based on function
            int dataBytes;
            if (function == ModbusFunction.ReadCoils || function == ModbusFunction.ReadDiscreteInputs)
                dataBytes = (quantity + 7) / 8; // bit-packed
            else
                dataBytes = quantity * 2; // register data

            int expectedMinResponse;
            if (_transport == ModbusTransport.Tcp)
                expectedMinResponse = 9 + dataBytes; // MBAP(7) + func(1) + byteCount(1) + data
            else
                expectedMinResponse = 3 + dataBytes + 2; // addr(1) + func(1) + byteCount(1) + data + CRC(2)

            lock (_lock)
            {
                Connect();
                _port.Clear();

                byte[] request;
                if (_transport == ModbusTransport.Tcp)
                {
                    var txId = _transactionId++;
                    request = ModbusFrame.BuildTcpRequest(txId, unitId, function, startAddress, quantity);
                }
                else
                {
                    request = ModbusFrame.BuildRtuRequest(unitId, function, startAddress, quantity);
                }

                _port.Write(request);
                return WaitAndParseResponse(unitId, expectedMinResponse);
            }
        }

        private ModbusResponse SendWriteRequest(byte unitId, byte[] pdu, int expectedMinResponse)
        {
            lock (_lock)
            {
                Connect();
                _port.Clear();

                byte[] request;
                if (_transport == ModbusTransport.Tcp)
                {
                    var txId = _transactionId++;
                    request = ModbusFrame.BuildTcpWriteRequest(txId, unitId, pdu);
                    expectedMinResponse = 7 + 5; // MBAP(7) + func(1) + addr(2) + value(2)
                }
                else
                {
                    request = ModbusFrame.BuildRtuWriteRequest(unitId, pdu);
                    expectedMinResponse = 1 + 5 + 2; // unit(1) + func(1) + addr(2) + value(2) + CRC(2)
                }

                _port.Write(request);
                return WaitAndParseResponse(unitId, expectedMinResponse);
            }
        }

        private ModbusResponse WaitAndParseResponse(byte unitId, int expectedMinResponse)
        {
            var elapsed = 0;
            while (!_port.HasReadData(expectedMinResponse) && elapsed < ResponseTimeoutMs)
            {
                Thread.Sleep(PollIntervalMs);
                elapsed += PollIntervalMs;
            }

            if (!_port.HasReadData(5)) // absolute minimum for any response
                throw new TimeoutException($"Modbus response timeout ({ResponseTimeoutMs}ms) from unit {unitId}");

            var responseData = _port.RemoveReadData(_port.GetData().Length);

            return _transport == ModbusTransport.Tcp
                ? ModbusFrame.ParseTcpResponse(responseData)
                : ModbusFrame.ParseRtuResponse(responseData);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
