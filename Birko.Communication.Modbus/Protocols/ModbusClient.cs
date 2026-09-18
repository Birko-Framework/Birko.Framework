using System;
using System.IO;
using System.Threading;
using Birko.Communication.Ports;

namespace Birko.Communication.Modbus.Protocols
{
    /// <summary>
    /// Modbus master client over any IPort (TCP via TcpIp, RTU via Serial).
    /// Thread-safe — one request at a time via lock.
    /// <para><b>Synchronous by design (CR-L066):</b> read/write calls block, and the response wait polls
    /// on <see cref="Thread.Sleep"/> (<see cref="PollIntervalMs"/>) up to <see cref="ResponseTimeoutMs"/>
    /// with no <c>CancellationToken</c> — inherent to the synchronous <see cref="IPort"/> contract. For a
    /// hosted/background-service caller, run these on a dedicated thread or wrap in <c>Task.Run</c>.</para>
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
            return SendWriteRequest(unitId, pdu); // response: func(1) + addr(2) + value(2) = 5 PDU bytes
        }

        /// <summary>
        /// Writes a single register (function 0x06).
        /// </summary>
        public ModbusResponse WriteSingleRegister(byte unitId, ushort address, ushort value)
        {
            var pdu = ModbusFrame.BuildWriteSinglePdu(ModbusFunction.WriteSingleRegister, address, value);
            return SendWriteRequest(unitId, pdu);
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
            return SendWriteRequest(unitId, pdu); // response: func(1) + addr(2) + quantity(2) = 5 PDU bytes
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
            return SendWriteRequest(unitId, pdu);
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
                ushort? txId = null;
                if (_transport == ModbusTransport.Tcp)
                {
                    txId = _transactionId++;
                    request = ModbusFrame.BuildTcpRequest(txId.Value, unitId, function, startAddress, quantity);
                }
                else
                {
                    request = ModbusFrame.BuildRtuRequest(unitId, function, startAddress, quantity);
                }

                _port.Write(request);
                return WaitAndParseResponse(unitId, expectedMinResponse, txId);
            }
        }

        private ModbusResponse SendWriteRequest(byte unitId, byte[] pdu)
        {
            lock (_lock)
            {
                Connect();
                _port.Clear();

                byte[] request;
                ushort? txId = null;
                int expectedMinResponse; // CR-L065: computed here, not taken as a (always-overwritten) parameter
                if (_transport == ModbusTransport.Tcp)
                {
                    txId = _transactionId++;
                    request = ModbusFrame.BuildTcpWriteRequest(txId.Value, unitId, pdu);
                    expectedMinResponse = 7 + 5; // MBAP(7) + func(1) + addr(2) + value(2)
                }
                else
                {
                    request = ModbusFrame.BuildRtuWriteRequest(unitId, pdu);
                    expectedMinResponse = 1 + 5 + 2; // unit(1) + func(1) + addr(2) + value(2) + CRC(2)
                }

                _port.Write(request);
                return WaitAndParseResponse(unitId, expectedMinResponse, txId);
            }
        }

        private ModbusResponse WaitAndParseResponse(byte unitId, int expectedMinResponse, ushort? expectedTransactionId = null)
        {
            var elapsed = 0;
            // Also stop early once a COMPLETE (short) error response has arrived, instead of spinning
            // the full ResponseTimeoutMs — an exception frame never satisfies HasReadData(expectedMinResponse),
            // which is sized for a successful reply (CR-L067).
            while (!_port.HasReadData(expectedMinResponse) && !IsCompleteErrorResponse() && elapsed < ResponseTimeoutMs)
            {
                Thread.Sleep(PollIntervalMs);
                elapsed += PollIntervalMs;
            }

            // If the full expected frame never arrived, only proceed when the buffer holds a
            // COMPLETE Modbus error response (which is legitimately shorter than
            // expectedMinResponse). Otherwise the bytes are a truncated normal frame and parsing
            // them yields a spurious CRC mismatch (RTU) or mis-framing (TCP) — treat as a timeout
            // (CR-H025).
            if (!_port.HasReadData(expectedMinResponse) && !IsCompleteErrorResponse())
                throw new TimeoutException($"Modbus response timeout ({ResponseTimeoutMs}ms) from unit {unitId}");

            var responseData = _port.RemoveReadData(_port.GetData().Length);

            if (_transport != ModbusTransport.Tcp)
                return ModbusFrame.ParseRtuResponse(responseData);

            var tcpResponse = ModbusFrame.ParseTcpResponse(responseData);

            // Correlate the MBAP transaction id: a stale/out-of-order frame (e.g. a late reply left
            // over after a prior timeout) must not be accepted as this request's response (CR-M052).
            if (expectedTransactionId.HasValue && tcpResponse.TransactionId != expectedTransactionId.Value)
            {
                throw new IOException(
                    $"Modbus TCP transaction id mismatch from unit {unitId}: expected {expectedTransactionId.Value}, got {tcpResponse.TransactionId} (stale/out-of-order response).");
            }

            return tcpResponse;
        }

        /// <summary>
        /// True when the buffered bytes are a complete Modbus exception response — the function-code
        /// byte has its high bit set and the full (short) error frame is present. Used to accept a
        /// response shorter than the expected normal frame without parsing a truncated frame.
        /// </summary>
        private bool IsCompleteErrorResponse()
        {
            var data = _port.GetData();
            // RTU error: unit(1)+func(1)+exc(1)+CRC(2)=5, func at index 1.
            // TCP error: MBAP(7)+func(1)+exc(1)=9, func at index 7.
            int funcIndex = _transport == ModbusTransport.Tcp ? 7 : 1;
            int errorFrameLen = _transport == ModbusTransport.Tcp ? 9 : 5;

            if (data.Length < errorFrameLen)
                return false;
            return (data[funcIndex] & 0x80) != 0;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
