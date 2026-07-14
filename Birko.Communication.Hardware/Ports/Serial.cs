using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using Birko.Communication.Ports;

namespace Birko.Communication.Hardware.Ports
{
    public class SerialSettings : PortSettings
    {
        public int BaudRate { get; set; } = 9600;
        public Parity Parity { get; set; }
        public int DataBits { get; set; }
        public StopBits StopBits { get; set; }

        public override string GetID()
        {
            return string.Format("SerialPort|{0}|{1}|{2}|{3}|{4}", Name, BaudRate, Parity, DataBits, StopBits);
        }
    }

    public class Serial : AbstractPort, IDisposable
    {
        private readonly SerialPort port;
        private bool _disposed;

        // ReadData is touched by both the DataReceived threadpool thread (ReadSerial appends) and
        // caller threads (Read/RemoveReadData/Clear). List<byte> is not thread-safe, so every access
        // is guarded by this single lock (CR-M049) — previously only ReadSerial locked (on `port`).
        private readonly object _readLock = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="Serial"/> class.
        /// </summary>
        /// <param name="settings">The settings.</param>
        public Serial(SerialSettings settings) : base(settings)
        {
            // Guard so `port` is always non-null; without this a null settings left every method
            // dereferencing a null port with an NRE (CR-M048).
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            port = new SerialPort(settings.Name, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits);
            port.RtsEnable = true;
            port.DtrEnable = true;
        }

        public override void Write(byte[] data)
        {
            if (!port.IsOpen)
                Open();
            port.Write(data, 0, data.Length);
        }

        public override byte[] Read(int size)
        {
            ReadSerial();
            lock (_readLock)
            {
                if (size < 0)
                {
                    return ReadData.Count > 0 ? ReadData.GetRange(0, ReadData.Count).ToArray() : new byte[0];
                }

                return ReadData.Count >= size ? ReadData.GetRange(0, size).ToArray() : new byte[0];
            }
        }

        public override void Open()
        {
            if (!IsOpen() || !port.IsOpen)
            {
                try
                {
                    port.Open();
                    Clear();
                    port.DataReceived += new SerialDataReceivedEventHandler(DataReceviedHandler);
                    _isOpen = true;
                }
                catch (UnauthorizedAccessException)
                {
                    _isOpen = false;
                }
                // CR-L060: removed a no-op `catch (Exception) { throw; }` — other exceptions propagate naturally.
            }
            // osetrenie ak hodi chyby ze niekto pouziva port vrati false
        }

        public override void Clear()
        {
            lock (_readLock)
            {
                base.Clear();
            }
            if (port.IsOpen)
            {
                byte[] buffer = new byte[port.BytesToRead];
                port.Read(buffer, 0, buffer.Length);
            }
        }
        public override void Close()
        {
            if (!IsOpen()) return;
            // Unsubscribe before closing so no late DataReceived callback fires mid-close.
            port.DataReceived -= new SerialDataReceivedEventHandler(DataReceviedHandler);
            port.Close();
            _isOpen = false;
        }

        /// <summary>
        /// Releases the underlying <see cref="SerialPort"/> (an IDisposable holding an unmanaged OS
        /// handle). Close() alone never disposed it, leaking handles across open/close cycles (CR-H023).
        /// </summary>
        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing && port != null)
            {
                try { port.DataReceived -= new SerialDataReceivedEventHandler(DataReceviedHandler); } catch { }
                port.Dispose();
                _isOpen = false;
            }
        }

        /// <summary>
        /// The data recevied handler.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.IO.Ports.SerialDataReceivedEventArgs"/> instance containing the event data.</param>
        private void DataReceviedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            ReadSerial();
            InvokeProcessData();
        }

        /// <summary>
        /// Reads bytes from  serial port into buffer.
        /// </summary>
        protected void ReadSerial()
        {
            // Serialize both the port read and the ReadData append under one lock so concurrent
            // DataReceived-thread and caller-thread reads don't race the port or corrupt the buffer.
            lock (_readLock)
            {
                if (port.IsOpen && port.BytesToRead > 0)
                {
                    byte[] buffer = new byte[port.BytesToRead];
                    int read = port.Read(buffer, 0, buffer.Length);
                    for (int i = 0; i < read; i++)
                        ReadData.Add(buffer[i]);
                }
            }
        }

        public override bool HasReadData(int size)
        {
            lock (_readLock)
            {
                if (size < 0) return ReadData.Count > 0;
                return ReadData.Count >= size;
            }
        }

        public override byte[] RemoveReadData(int size)
        {
            ReadSerial();
            // Atomic copy-and-remove under the buffer lock — the removed range matches what is
            // returned, and RemoveRange(0, -1) can no longer throw for size < 0 (CR-M049).
            lock (_readLock)
            {
                int count = size < 0 ? ReadData.Count : Math.Min(size, ReadData.Count);
                if (count <= 0) return new byte[0];

                byte[] result = ReadData.GetRange(0, count).ToArray();
                ReadData.RemoveRange(0, count);
                return result;
            }
        }
    }
}
