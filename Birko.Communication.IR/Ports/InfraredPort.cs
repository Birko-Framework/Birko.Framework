using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.Ports;
using Birko.Communication.IR.Protocols;
using Birko.Communication.IR.Transports;

namespace Birko.Communication.IR.Ports
{
    /// <summary>
    /// Consumer IR port — 38 kHz modulated infrared for remote control (NEC, Samsung, RC5).
    /// NOT IrDA/IrCOMM — see Birko.Communication.Hardware.Ports.Infraport for serial IrCOMM.
    /// </summary>
    public class InfraredPort : AbstractPort
    {
        private readonly IIrTransport _transport;
        private readonly List<IIrProtocol> _protocols = new();

        /// <summary>
        /// The IR transport backend used for sending/receiving raw signals.
        /// </summary>
        public IIrTransport Transport => _transport;

        /// <summary>
        /// Registered protocols for encoding/decoding.
        /// </summary>
        public IReadOnlyList<IIrProtocol> Protocols => _protocols;

        /// <summary>
        /// Raised when an IR command is decoded from a received signal.
        /// </summary>
        public event EventHandler<IrCommand>? OnCommandReceived;

        public InfraredPort(InfraredSettings settings, IIrTransport transport) : base(settings)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.OnReceived += HandleReceivedTiming;
        }

        /// <summary>
        /// Register a protocol for decoding received signals.
        /// </summary>
        public void RegisterProtocol(IIrProtocol protocol)
        {
            if (protocol == null)
            {
                throw new ArgumentNullException(nameof(protocol));
            }
            _protocols.Add(protocol);
        }

        /// <summary>
        /// Send an IR command using the specified protocol.
        /// </summary>
        public async Task SendCommandAsync(IIrProtocol protocol, IrCommand command, CancellationToken cancellationToken = default)
        {
            if (protocol == null)
            {
                throw new ArgumentNullException(nameof(protocol));
            }
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var timing = protocol.Encode(command);
            await _transport.TransmitAsync(timing, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send raw IR timings directly (bypass protocol encoding).
        /// </summary>
        public async Task SendRawAsync(IrTiming timing, CancellationToken cancellationToken = default)
        {
            if (timing == null)
            {
                throw new ArgumentNullException(nameof(timing));
            }
            await _transport.TransmitAsync(timing, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Enter learning mode — listen for incoming IR signals and decode them.
        /// </summary>
        public async Task StartLearningAsync(CancellationToken cancellationToken = default)
        {
            await _transport.StartReceiveAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Exit learning mode.
        /// </summary>
        public async Task StopLearningAsync(CancellationToken cancellationToken = default)
        {
            await _transport.StopReceiveAsync(cancellationToken).ConfigureAwait(false);
        }

        public override void Open()
        {
            _transport.ConnectAsync().GetAwaiter().GetResult();
            _isOpen = true;
        }

        public override void Close()
        {
            _transport.StopReceiveAsync().GetAwaiter().GetResult();
            _transport.DisconnectAsync().GetAwaiter().GetResult();
            _isOpen = false;
        }

        public override bool IsOpen()
        {
            return _transport.IsConnected;
        }

        public override void Write(byte[] data)
        {
            // For raw byte-level writes, wrap in IrTiming.
            // Expects packed int[] durations serialized as little-endian 4-byte ints.
            if (data.Length % 4 != 0)
            {
                throw new ArgumentException("Data length must be a multiple of 4 (packed int32 durations).", nameof(data));
            }

            int count = data.Length / 4;
            var durations = new int[count];
            for (int i = 0; i < count; i++)
            {
                durations[i] = BitConverter.ToInt32(data, i * 4);
            }

            var settings = (InfraredSettings)Settings;
            var timing = new IrTiming(durations, settings.CarrierFrequencyHz);
            _transport.TransmitAsync(timing).GetAwaiter().GetResult();
        }

        public override byte[] Read(int size)
        {
            if (ReadData.Count >= size)
            {
                var result = ReadData.GetRange(0, size).ToArray();
                return result;
            }
            return Array.Empty<byte>();
        }

        public override bool HasReadData(int size)
        {
            return ReadData.Count >= size;
        }

        public override byte[] RemoveReadData(int size)
        {
            if (ReadData.Count < size)
            {
                return Array.Empty<byte>();
            }
            var result = ReadData.GetRange(0, size).ToArray();
            ReadData.RemoveRange(0, size);
            return result;
        }

        private void HandleReceivedTiming(object? sender, IrTiming timing)
        {
            // Try each registered protocol for decoding
            foreach (var protocol in _protocols)
            {
                var command = protocol.Decode(timing);
                if (command != null)
                {
                    OnCommandReceived?.Invoke(this, command);
                    InvokeProcessData();
                    return;
                }
            }

            // No protocol matched — store raw timing bytes in ReadData for low-level consumers
            foreach (var duration in timing.Durations)
            {
                var bytes = BitConverter.GetBytes(duration);
                ReadData.AddRange(bytes);
            }
            InvokeProcessData();
        }
    }
}
