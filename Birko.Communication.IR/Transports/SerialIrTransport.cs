using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.IR.Protocols;
using Birko.Communication.Ports;
using Birko.Communication.Hardware.Ports;

namespace Birko.Communication.IR.Transports
{
    /// <summary>
    /// IR transport over USB-UART with a microcontroller (Arduino/ESP32) driving an IR LED.
    /// The microcontroller expects a simple text protocol:
    ///   TX: "SEND {freq} {duration0},{duration1},...\n"
    ///   RX: "RECV {freq} {duration0},{duration1},...\n"
    ///   RX: "OK\n" (after successful send)
    /// </summary>
    public class SerialIrTransport : IIrTransport
    {
        private readonly Serial _serial;
        private bool _isConnected;
        private bool _isReceiving;
        private CancellationTokenSource? _receiveCts;
        private Task? _receiveTask;

        public string Name => "Serial";
        public bool IsConnected => _isConnected && _serial.IsOpen();

        public event EventHandler<IrTiming>? OnReceived;

        /// <summary>
        /// Create a serial IR transport on the specified COM port.
        /// </summary>
        /// <param name="portName">Serial port name (e.g., "COM3", "/dev/ttyUSB0").</param>
        /// <param name="baudRate">Baud rate for the microcontroller (default 115200).</param>
        public SerialIrTransport(string portName, int baudRate = 115200)
        {
            var settings = new SerialSettings
            {
                Name = portName,
                BaudRate = baudRate,
                DataBits = 8,
                Parity = 0,
                StopBits = 1
            };
            _serial = new Serial(settings);
        }

        public SerialIrTransport(Serial serial)
        {
            _serial = serial ?? throw new ArgumentNullException(nameof(serial));
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (!_serial.IsOpen())
            {
                _serial.Open();
            }
            _isConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_isReceiving)
            {
                StopReceiveAsync(cancellationToken).GetAwaiter().GetResult();
            }
            if (_serial.IsOpen())
            {
                _serial.Close();
            }
            _isConnected = false;
            return Task.CompletedTask;
        }

        public Task TransmitAsync(IrTiming timing, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                throw new InvalidOperationException("Transport is not connected.");
            }

            var sb = new StringBuilder();
            sb.Append("SEND ");
            sb.Append(timing.CarrierFrequencyHz);
            sb.Append(' ');

            for (int i = 0; i < timing.Durations.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(timing.Durations[i]);
            }
            sb.Append('\n');

            var data = Encoding.ASCII.GetBytes(sb.ToString());
            _serial.Write(data);

            return Task.CompletedTask;
        }

        public Task StartReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                throw new InvalidOperationException("Transport is not connected.");
            }

            if (_isReceiving)
            {
                return Task.CompletedTask;
            }

            // Send learn command to microcontroller
            var cmd = Encoding.ASCII.GetBytes("LEARN\n");
            _serial.Write(cmd);

            _isReceiving = true;
            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveTask = Task.Run(() => ReceiveLoop(_receiveCts.Token), _receiveCts.Token);
            return Task.CompletedTask;
        }

        public Task StopReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (!_isReceiving)
            {
                return Task.CompletedTask;
            }

            // Send stop command
            if (_isConnected && _serial.IsOpen())
            {
                var cmd = Encoding.ASCII.GetBytes("STOP\n");
                _serial.Write(cmd);
            }

            _receiveCts?.Cancel();
            _isReceiving = false;
            return Task.CompletedTask;
        }

        private void ReceiveLoop(CancellationToken cancellationToken)
        {
            var lineBuffer = new StringBuilder();

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_serial.IsOpen() || !_serial.HasReadData(1))
                {
                    Thread.Sleep(10);
                    continue;
                }

                var data = _serial.RemoveReadData(_serial.ReadData.Count);
                var text = Encoding.ASCII.GetString(data);

                foreach (char c in text)
                {
                    if (c == '\n')
                    {
                        var line = lineBuffer.ToString().Trim();
                        lineBuffer.Clear();
                        ProcessReceivedLine(line);
                    }
                    else if (c != '\r')
                    {
                        lineBuffer.Append(c);
                    }
                }
            }
        }

        private void ProcessReceivedLine(string line)
        {
            // Expected: "RECV {freq} {d0},{d1},{d2},..."
            if (!line.StartsWith("RECV ", StringComparison.Ordinal))
            {
                return;
            }

            var parts = line.Substring(5).Split(' ', 2);
            if (parts.Length < 2)
            {
                return;
            }

            if (!int.TryParse(parts[0], out int freq))
            {
                return;
            }

            var durationParts = parts[1].Split(',');
            var durations = new List<int>();

            foreach (var dp in durationParts)
            {
                if (int.TryParse(dp.Trim(), out int d))
                {
                    durations.Add(d);
                }
            }

            if (durations.Count > 0)
            {
                var timing = new IrTiming(durations.ToArray(), freq);
                OnReceived?.Invoke(this, timing);
            }
        }

        public void Dispose()
        {
            _receiveCts?.Cancel();
            _receiveCts?.Dispose();
            if (_serial.IsOpen())
            {
                _serial.Close();
            }
        }
    }
}
