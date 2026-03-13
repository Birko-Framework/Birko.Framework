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

    public class Serial : AbstractPort
    {
        private readonly SerialPort port = null!;

        /// <summary>
        /// Initializes a new instance of the <see cref="Serial"/> class.
        /// </summary>
        /// <param name="settings">The settings.</param>
        public Serial(SerialSettings settings) : base(settings)
        {
            if (settings != null)
            {
                port = new SerialPort(settings.Name, settings.BaudRate ,settings.Parity, settings.DataBits, settings.StopBits);
                port.RtsEnable = true;
                port.DtrEnable = true;
            }
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
            if (HasReadData(size))
            {
                if (size < 0)
                {
                    return ReadData.GetRange(0, ReadData.Count).ToArray();
                }
                else
                {
                    return ReadData.GetRange(0, size).ToArray();
                }
            }
            else
            {
                return new byte[0];
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
                catch (Exception)
                {
                    throw;
                }
            }
            // osetrenie ak hodi chyby ze niekto pouziva port vrati false
        }

        public override void Clear()
        {
            base.Clear();
            if (port.IsOpen)
            {
                byte[] buffer = new byte[port.BytesToRead];
                port.Read(buffer, 0, buffer.Length);
            }
        }
        public override void Close()
        {
            if (!IsOpen()) return;
            port.Close();
            port.DataReceived -= new SerialDataReceivedEventHandler(DataReceviedHandler);
            _isOpen = false;
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
            lock (port)
            {
                if (port.BytesToRead > 0)
                {
                    byte[] buffer = new byte[port.BytesToRead];
                    port.Read(buffer, 0, buffer.Length);
                    ReadData.AddRange(buffer);
                }
            }
        }

        public override bool HasReadData(int size)
        {
            return (ReadData.Count >= size);
        }

        public override byte[] RemoveReadData(int size)
        {
            byte[] result = Read(size);
            if (HasReadData(size))
            {
                ReadData.RemoveRange(0, size);
            }
            return result;
        }
    }
}
