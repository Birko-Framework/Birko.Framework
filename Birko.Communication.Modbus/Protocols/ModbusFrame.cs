using System;

namespace Birko.Communication.Modbus.Protocols
{
    /// <summary>
    /// Builds and parses Modbus frames for TCP (MBAP header) and RTU (CRC-16) transports.
    /// </summary>
    public static class ModbusFrame
    {
        // ── TCP (MBAP) ──

        public static byte[] BuildTcpRequest(ushort transactionId, byte unitId, ModbusFunction function, ushort startAddress, ushort quantity)
        {
            var pdu = BuildReadPdu(function, startAddress, quantity);
            return WrapTcpFrame(transactionId, unitId, pdu);
        }

        public static byte[] BuildTcpWriteRequest(ushort transactionId, byte unitId, byte[] pdu)
        {
            return WrapTcpFrame(transactionId, unitId, pdu);
        }

        public static ModbusResponse ParseTcpResponse(byte[] data)
        {
            if (data.Length < 9)
                throw new ModbusException(0);

            var transactionId = (ushort)((data[0] << 8) | data[1]);
            var unitId = data[6];
            var functionCode = data[7];

            // Error response: function code has high bit set
            if ((functionCode & 0x80) != 0)
                throw new ModbusException(data[8]);

            var function = (ModbusFunction)functionCode;

            if (IsWriteFunction(function))
            {
                // Write response: function + address(2) + value/quantity(2) = 4 bytes payload
                var values = new byte[4];
                Array.Copy(data, 8, values, 0, Math.Min(4, data.Length - 8));
                return new ModbusResponse(transactionId, unitId, function, values);
            }
            else
            {
                // Read response: function + byteCount + data[byteCount]
                var byteCount = data[8];
                var values = new byte[byteCount];
                Array.Copy(data, 9, values, 0, byteCount);
                return new ModbusResponse(transactionId, unitId, function, values);
            }
        }

        // ── RTU (CRC-16) ──

        public static byte[] BuildRtuRequest(byte unitId, ModbusFunction function, ushort startAddress, ushort quantity)
        {
            var pdu = BuildReadPdu(function, startAddress, quantity);
            return WrapRtuFrame(unitId, pdu);
        }

        public static byte[] BuildRtuWriteRequest(byte unitId, byte[] pdu)
        {
            return WrapRtuFrame(unitId, pdu);
        }

        public static ModbusResponse ParseRtuResponse(byte[] data)
        {
            if (data.Length < 5) // unit(1) + function(1) + min payload + crc(2)
                throw new ModbusException(0);

            // Verify CRC
            var receivedCrc = (ushort)(data[^2] | (data[^1] << 8));
            var calculatedCrc = CalculateCrc16(data, data.Length - 2);
            if (receivedCrc != calculatedCrc)
                throw new InvalidOperationException("Modbus RTU CRC mismatch");

            var unitId = data[0];
            var functionCode = data[1];

            if ((functionCode & 0x80) != 0)
                throw new ModbusException(data[2]);

            var function = (ModbusFunction)functionCode;

            if (IsWriteFunction(function))
            {
                // Write response: function + address(2) + value/quantity(2) = 4 bytes payload
                var values = new byte[4];
                Array.Copy(data, 2, values, 0, Math.Min(4, data.Length - 4)); // exclude unit, func, crc(2)
                return new ModbusResponse(0, unitId, function, values);
            }
            else
            {
                // Read response: function + byteCount + data[byteCount]
                var byteCount = data[2];
                var values = new byte[byteCount];
                Array.Copy(data, 3, values, 0, byteCount);
                return new ModbusResponse(0, unitId, function, values);
            }
        }

        // ── Shared ──

        private static byte[] BuildReadPdu(ModbusFunction function, ushort startAddress, ushort quantity)
        {
            return new byte[]
            {
                (byte)function,
                (byte)(startAddress >> 8),
                (byte)(startAddress & 0xFF),
                (byte)(quantity >> 8),
                (byte)(quantity & 0xFF)
            };
        }

        /// <summary>
        /// Builds a PDU for WriteSingleCoil (0x05) or WriteSingleRegister (0x06).
        /// </summary>
        public static byte[] BuildWriteSinglePdu(ModbusFunction function, ushort address, ushort value)
        {
            return new byte[]
            {
                (byte)function,
                (byte)(address >> 8),
                (byte)(address & 0xFF),
                (byte)(value >> 8),
                (byte)(value & 0xFF)
            };
        }

        /// <summary>
        /// Builds a PDU for WriteMultipleCoils (0x0F) or WriteMultipleRegisters (0x10).
        /// </summary>
        public static byte[] BuildWriteMultiplePdu(ModbusFunction function, ushort startAddress, ushort quantity, byte[] data)
        {
            var pdu = new byte[6 + data.Length];
            pdu[0] = (byte)function;
            pdu[1] = (byte)(startAddress >> 8);
            pdu[2] = (byte)(startAddress & 0xFF);
            pdu[3] = (byte)(quantity >> 8);
            pdu[4] = (byte)(quantity & 0xFF);
            pdu[5] = (byte)data.Length;
            Array.Copy(data, 0, pdu, 6, data.Length);
            return pdu;
        }

        private static byte[] WrapTcpFrame(ushort transactionId, byte unitId, byte[] pdu)
        {
            // MBAP header: Transaction(2) + Protocol(2) + Length(2) + UnitId(1) = 7 bytes
            var frame = new byte[7 + pdu.Length];

            frame[0] = (byte)(transactionId >> 8);
            frame[1] = (byte)(transactionId & 0xFF);
            frame[2] = 0; // Protocol ID high
            frame[3] = 0; // Protocol ID low
            var length = (ushort)(pdu.Length + 1); // PDU + unit ID
            frame[4] = (byte)(length >> 8);
            frame[5] = (byte)(length & 0xFF);
            frame[6] = unitId;
            Array.Copy(pdu, 0, frame, 7, pdu.Length);

            return frame;
        }

        private static byte[] WrapRtuFrame(byte unitId, byte[] pdu)
        {
            var frame = new byte[1 + pdu.Length + 2]; // unit ID + PDU + CRC
            frame[0] = unitId;
            Array.Copy(pdu, 0, frame, 1, pdu.Length);

            var crc = CalculateCrc16(frame, frame.Length - 2);
            frame[^2] = (byte)(crc & 0xFF);        // CRC low byte first
            frame[^1] = (byte)((crc >> 8) & 0xFF);  // CRC high byte

            return frame;
        }

        private static bool IsWriteFunction(ModbusFunction function)
        {
            return function == ModbusFunction.WriteSingleCoil
                || function == ModbusFunction.WriteSingleRegister
                || function == ModbusFunction.WriteMultipleCoils
                || function == ModbusFunction.WriteMultipleRegisters;
        }

        /// <summary>
        /// Standard Modbus CRC-16 (polynomial 0xA001).
        /// </summary>
        public static ushort CalculateCrc16(byte[] data, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc >>= 1;
                }
            }
            return crc;
        }
    }

    public sealed class ModbusResponse
    {
        public ushort TransactionId { get; }
        public byte UnitId { get; }
        public ModbusFunction Function { get; }
        public byte[] Data { get; }

        public ModbusResponse(ushort transactionId, byte unitId, ModbusFunction function, byte[] data)
        {
            TransactionId = transactionId;
            UnitId = unitId;
            Function = function;
            Data = data;
        }

        /// <summary>
        /// Reads 16-bit register values from the raw response data.
        /// Each register is 2 bytes, big-endian.
        /// </summary>
        public ushort[] GetRegisters()
        {
            var count = Data.Length / 2;
            var registers = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                registers[i] = (ushort)((Data[i * 2] << 8) | Data[i * 2 + 1]);
            }
            return registers;
        }

        /// <summary>
        /// Unpacks coil/discrete input bit data into boolean array.
        /// </summary>
        public bool[] GetCoils(int quantity)
        {
            var coils = new bool[quantity];
            for (int i = 0; i < quantity; i++)
            {
                var byteIndex = i / 8;
                var bitIndex = i % 8;
                if (byteIndex < Data.Length)
                    coils[i] = (Data[byteIndex] & (1 << bitIndex)) != 0;
            }
            return coils;
        }

        /// <summary>
        /// Reads a 32-bit float from two consecutive registers (big-endian word order).
        /// </summary>
        public float GetFloat32(int registerOffset = 0)
        {
            var byteOffset = registerOffset * 2;
            if (byteOffset + 4 > Data.Length)
                throw new InvalidOperationException("Not enough data for Float32");

            var bytes = new byte[4];
            // Big-endian word order (AB CD)
            bytes[3] = Data[byteOffset];
            bytes[2] = Data[byteOffset + 1];
            bytes[1] = Data[byteOffset + 2];
            bytes[0] = Data[byteOffset + 3];
            return BitConverter.ToSingle(bytes, 0);
        }

        /// <summary>
        /// Reads a 32-bit signed integer from two consecutive registers (big-endian).
        /// </summary>
        public int GetInt32(int registerOffset = 0)
        {
            var byteOffset = registerOffset * 2;
            if (byteOffset + 4 > Data.Length)
                throw new InvalidOperationException("Not enough data for Int32");

            return (Data[byteOffset] << 24) | (Data[byteOffset + 1] << 16) |
                   (Data[byteOffset + 2] << 8) | Data[byteOffset + 3];
        }
    }
}
