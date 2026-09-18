using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.Ports;

namespace Birko.Communication.WebSocket.Ports
{
    public class WebSocketSettings : PortSettings
    {
        public string Uri { get; set; } = string.Empty;

        public override string GetID()
        {
            return string.Format("WebSocket|{0}|{1}", Name, Uri);
        }
    }

    public class WebSocketPort : AbstractPort
    {
        private ClientWebSocket? _socket;
        private Thread? _readThread;
        private bool _stopThread;
        private CancellationTokenSource? _cts;

        public WebSocketPort(WebSocketSettings settings) : base(settings)
        {
        }

        public override void Write(byte[] data)
        {
            if (_socket == null || _socket.State != WebSocketState.Open)
                Open();

            if (_socket != null && _socket.State == WebSocketState.Open)
            {
                // AbstractPort.Write is synchronous, so we block — but via ConfigureAwait(false) +
                // GetAwaiter().GetResult() so the original WebSocketException propagates (not an
                // AggregateException from .Wait()) and no captured SynchronizationContext is required
                // (CR-M073).
                var segment = new ArraySegment<byte>(data);
                _socket.SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }

        public override byte[] Read(int size)
        {
            if (HasReadData(size))
            {
                lock (ReadData)
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
            }
            return new byte[0];
        }

        public override void Open()
        {
             if (!IsOpen())
            {
                var settings = Settings as WebSocketSettings;
                if (settings == null) throw new InvalidOperationException("Invalid Settings for WebSocket port");

                try
                {
                    _socket = new ClientWebSocket();
                    _cts = new CancellationTokenSource();

                    var uri = new Uri(settings.Uri);
                    _socket.ConnectAsync(uri, _cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();

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
                _cts?.Cancel();

                if (_socket != null)
                {
                    if (_socket.State == WebSocketState.Open)
                    {
                         try
                         {
                            _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                                .ConfigureAwait(false).GetAwaiter().GetResult();
                         }
                         catch {}
                    }
                    _socket.Dispose();
                    _socket = null;
                }
                _isOpen = false;
            }
        }

         private void ReadWorker()
        {
            var buffer = new byte[4096];

            while (!_stopThread && _socket != null && _socket.State == WebSocketState.Open)
            {
                try
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var result = _socket!.ReceiveAsync(segment, _cts!.Token)
                        .ConfigureAwait(false).GetAwaiter().GetResult();

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Close();
                        break;
                    }

                    if (result.Count > 0)
                    {
                        byte[] received = new byte[result.Count];
                        Array.Copy(buffer, received, result.Count);
                         lock (ReadData)
                        {
                            ReadData.AddRange(received);
                        }
                        InvokeProcessData();
                    }
                }
                catch
                {
                    if(_stopThread) break;
                    // Reconnect logic could be here, but for now just exit loop
                    break;
                }
            }
        }

        public override bool HasReadData(int size)
        {
            lock (ReadData)
            {
                if (size < 0)
                    return ReadData.Count > 0; // "all available" — true only when there is data (CR-H036)
                return ReadData.Count >= size;
            }
        }

        public override byte[] RemoveReadData(int size)
        {
            // Read + remove atomically under one lock, removing exactly what was read (whole buffer
            // for size < 0) so RemoveRange(0, -1) can't throw and the ReadWorker can't change Count
            // between the read and the remove (CR-H036).
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
