using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Birko.Communication.Ports;

namespace Birko.Communication.Network.Ports
{
    public class UdpSettings : PortSettings
    {
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; }
        public int LocalPort { get; set; } // For receiving

        public override string GetID()
        {
            return string.Format("Udp|{0}|{1}|{2}|{3}", Name, Address, Port, LocalPort);
        }
    }

    public class Udp : AbstractPort
    {
        private UdpClient? _client;
        private IPEndPoint? _remoteEndPoint;
        private Thread? _readThread;
        private bool _stopThread;

        public Udp(UdpSettings settings) : base(settings)
        {
        }

        public override void Write(byte[] data)
        {
            if (_client == null)
                Open();

            if (_client != null)
            {
                _client.Send(data, data.Length, _remoteEndPoint);
            }
        }

        public override byte[] Read(int size)
        {
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
                var settings = Settings as UdpSettings;
                if (settings == null) throw new InvalidOperationException("Invalid Settings for Udp port");

                try
                {
                    // Bind to local port if specified, otherwise 0 (any available)
                    _client = new UdpClient(settings.LocalPort);
                    _remoteEndPoint = new IPEndPoint(IPAddress.Parse(settings.Address), settings.Port);

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
            while (!_stopThread && _client != null)
            {
                try
                {
                    // UdpClient.Receive blocks, so this thread will wait.
                    // To shutdown cleanly, Close() disposes client which causes Receive to throw.
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] received = _client.Receive(ref remote);

                    if (received != null && received.Length > 0)
                    {
                        lock (ReadData)
                        {
                            ReadData.AddRange(received);
                        }
                        InvokeProcessData();
                    }
                }
                catch
                {
                     // Expected during close
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
