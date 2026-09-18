using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.Ports;

namespace Birko.Communication.Network.Ports
{
    public class TcpIpSettings : PortSettings
    {
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; }

        public override string GetID()
        {
            return string.Format("TcpIp|{0}|{1}|{2}", Name, Address, Port);
        }
    }

    public class TcpIp : AbstractPort
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private Thread? _readThread;
        private bool _stopThread;

        public TcpIp(TcpIpSettings settings) : base(settings)
        {
        }

        public override void Write(byte[] data)
        {
            if (_client == null || !_client.Connected)
                Open();

            if (_stream != null && _stream.CanWrite)
            {
                _stream.Write(data, 0, data.Length);
            }
        }

        public override byte[] Read(int size)
        {
            // Lock ReadData: the background ReadWorker mutates it under the same lock, and List<byte>
            // is not thread-safe (concurrent AddRange during GetRange can tear/throw) — CR-H026.
            lock (ReadData)
            {
                if (size < 0)
                {
                    return ReadData.ToArray();
                }
                if (ReadData.Count >= size)
                {
                    return ReadData.GetRange(0, size).ToArray();
                }
            }
            return new byte[0];
        }

        public override void Open()
        {
            if (IsOpen()) return;

            var settings = Settings as TcpIpSettings;
            if (settings == null) throw new InvalidOperationException("Invalid Settings for TcpIp port");

            try
            {
                _client = new TcpClient();
                _client.Connect(settings.Address, settings.Port);
                _stream = _client.GetStream();
                _isOpen = true;

                _stopThread = false;
                _readThread = new Thread(ReadWorker);
                _readThread.IsBackground = true;
                _readThread.Start();
            }
            catch (Exception)
            {
                _isOpen = false;
                throw;
            }
        }

        public override void Close()
        {
            if (!IsOpen()) return;

            // Signal stop and join the read worker (DataAvailable-gated, ≤50ms iterations) before
            // tearing down the stream/client, so Close returns deterministically rather than racing
            // the worker's field dereferences (CR-L069).
            _stopThread = true;
            _readThread?.Join(TimeSpan.FromMilliseconds(500));
            _readThread = null;

            if (_stream != null)
            {
                _stream.Close();
                _stream = null;
            }
            if (_client != null)
            {
                _client.Close();
                _client = null;
            }
            _isOpen = false;
        }

        private void ReadWorker()
        {
            byte[] buffer = new byte[1024];
            while (!_stopThread)
            {
                // Capture local references so a concurrent Close() nulling the fields can't cause an
                // NRE between the null check and the dereference (CR-L069).
                var stream = _stream;
                var client = _client;
                if (client == null || !client.Connected || stream == null)
                    break;
                try
                {
                    if (stream.DataAvailable)
                    {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            byte[] received = new byte[bytesRead];
                            Array.Copy(buffer, received, bytesRead);

                            lock (ReadData) // AbstractPort doesn't lock ReadData, but we should be safe
                            {
                                ReadData.AddRange(received);
                            }
                            InvokeProcessData();
                        }
                    }
                    else
                    {
                        Thread.Sleep(50);
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        public override bool HasReadData(int size)
        {
            lock (ReadData)
            {
                if (size < 0)
                    return ReadData.Count > 0; // "all available" — true only when there is data (CR-H027)
                return ReadData.Count >= size;
            }
        }

        public override byte[] RemoveReadData(int size)
        {
            // Read + RemoveRange atomically under the lock, removing exactly what was read (whole
            // buffer for size < 0) so RemoveRange(0, -1) can't throw (CR-H026 / CR-H027).
            lock (ReadData)
            {
                byte[] result = Read(size);
                if (result.Length > 0)
                {
                    ReadData.RemoveRange(0, result.Length);
                }
                return result;
            }
        }
    }
}
