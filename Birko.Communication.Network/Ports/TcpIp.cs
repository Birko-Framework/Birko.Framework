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
        public string Address { get; set; }
        public int Port { get; set; }

        public override string GetID()
        {
            return string.Format("TcpIp|{0}|{1}|{2}", Name, Address, Port);
        }
    }

    public class TcpIp : AbstractPort
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _readThread;
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
            // Reading is primarily handled by the background thread populating ReadData
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
            return new byte[0];
        }

        public override void Open()
        {
            if (!IsOpen())
            {
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
        }

        public override void Close()
        {
            if (IsOpen())
            {
                _stopThread = true;
                // Wait for thread to finish? Or just close stream which will throw in thread

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
        }

        private void ReadWorker()
        {
            byte[] buffer = new byte[1024];
            while (!_stopThread && _client != null && _client.Connected && _stream != null)
            {
                try
                {
                    if (_stream.DataAvailable)
                    {
                        int bytesRead = _stream.Read(buffer, 0, buffer.Length);
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
