using System;
using System.Collections.Generic;
using Birko.Communication.Ports;

namespace Birko.Communication.Modbus.Tests
{
    /// <summary>
    /// In-memory IPort mock for testing Modbus client without real hardware.
    /// Captures written data and provides pre-configured response data.
    /// </summary>
    public class MockPort : IPort
    {
        private readonly List<byte[]> _writtenData = new();
        private byte[] _readBuffer = Array.Empty<byte>();
        private bool _isOpen;

        /// <summary>
        /// All data written to this port (each Write call is one entry).
        /// </summary>
        public IReadOnlyList<byte[]> WrittenData => _writtenData;

        /// <summary>
        /// Set this before a read operation to simulate device response.
        /// </summary>
        public byte[] ResponseData
        {
            set => _readBuffer = value ?? Array.Empty<byte>();
        }

        public int OpenCount { get; private set; }
        public int CloseCount { get; private set; }
        public int ClearCount { get; private set; }

        public void Open()
        {
            _isOpen = true;
            OpenCount++;
        }

        public void Close()
        {
            _isOpen = false;
            CloseCount++;
        }

        public bool IsOpen() => _isOpen;

        public void Clear()
        {
            ClearCount++;
        }

        public bool IsEmpty() => _readBuffer.Length == 0;

        public byte[] GetData() => _readBuffer;

        public bool HasReadData(int size) => _readBuffer.Length >= size;

        public byte[] RemoveReadData(int size)
        {
            var result = new byte[Math.Min(size, _readBuffer.Length)];
            Array.Copy(_readBuffer, result, result.Length);

            if (size >= _readBuffer.Length)
            {
                _readBuffer = Array.Empty<byte>();
            }
            else
            {
                var remaining = new byte[_readBuffer.Length - size];
                Array.Copy(_readBuffer, size, remaining, 0, remaining.Length);
                _readBuffer = remaining;
            }

            return result;
        }

        public byte[] Read(int size) => RemoveReadData(size);

        public void Write(byte[] data)
        {
            var copy = new byte[data.Length];
            Array.Copy(data, copy, data.Length);
            _writtenData.Add(copy);
        }

        public void SubscribeProcessData(ProcessDataDelegate action) { }
        public void UnSubscribeProcessData(ProcessDataDelegate action) { }
    }
}
