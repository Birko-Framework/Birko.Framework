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
                 // WebSocket WriteAsync requires a task, we will wait for it synchronously to match the API
                 // or fire and forget if blocking is an issue, but standard Write implies blocking/completion.
                 try
                 {
                    var segment = new ArraySegment<byte>(data);
                    _socket.SendAsync(segment, WebSocketMessageType.Binary, true, CancellationToken.None).Wait();
                 }
                 catch (Exception)
                 {
                     throw;
                 }
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
                    _socket.ConnectAsync(uri, _cts.Token).Wait();

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
                            _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait();
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
                    // ReceiveAsync
                    var resultTask = _socket!.ReceiveAsync(segment, _cts!.Token);
                    resultTask.Wait();

                    var result = resultTask.Result;

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
            return (ReadData.Count >= size);
        }

        public override byte[] RemoveReadData(int size)
        {
            byte[] result = Read(size);
            if (HasReadData(size))
            {
                lock (ReadData)
                {
                    ReadData.RemoveRange(0, size);
                }
            }
            return result;
        }
    }
}
