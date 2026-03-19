using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.Ports;
using Birko.Communication.NFC.Models;
using Birko.Communication.NFC.Protocols;
using Birko.Communication.NFC.Transports;

namespace Birko.Communication.NFC.Ports
{
    /// <summary>
    /// NFC/RFID reader port — reads tag UIDs, NDEF data, and supports APDU passthrough.
    /// Wraps an <see cref="INfcTransport"/> backend and applies registered <see cref="INfcProtocol"/> parsers.
    /// </summary>
    public class NfcReaderPort : AbstractPort
    {
        private readonly INfcTransport _transport;
        private readonly List<INfcProtocol> _protocols = new();

        /// <summary>
        /// The NFC transport backend used for reading tags.
        /// </summary>
        public INfcTransport Transport => _transport;

        /// <summary>
        /// Registered protocols for tag data parsing.
        /// </summary>
        public IReadOnlyList<INfcProtocol> Protocols => _protocols;

        /// <summary>
        /// Raised when a tag is detected. The tag data includes parsed protocol information.
        /// </summary>
        public event EventHandler<NfcTagData>? OnTagDetected;

        /// <summary>
        /// Raised when a tag is removed from the reader field.
        /// </summary>
        public event EventHandler? OnTagRemoved;

        public NfcReaderPort(NfcReaderSettings settings, INfcTransport transport) : base(settings)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.TagDetected += HandleTagDetected;
            _transport.TagRemoved += HandleTagRemoved;
        }

        /// <summary>
        /// Register a protocol for parsing detected tags.
        /// </summary>
        public void RegisterProtocol(INfcProtocol protocol)
        {
            if (protocol == null)
            {
                throw new ArgumentNullException(nameof(protocol));
            }
            _protocols.Add(protocol);
        }

        /// <summary>
        /// Read a single tag. Blocks until a tag is presented or timeout expires.
        /// </summary>
        public async Task<NfcTagData?> ReadTagAsync(CancellationToken cancellationToken = default)
        {
            var nfcSettings = (NfcReaderSettings)Settings;
            var tag = await _transport.ReadTagAsync(nfcSettings.ReadTimeoutMs, cancellationToken).ConfigureAwait(false);
            if (tag != null)
            {
                ApplyProtocols(tag);
            }
            return tag;
        }

        /// <summary>
        /// Start continuous polling for tags.
        /// Detected tags trigger the <see cref="OnTagDetected"/> event.
        /// </summary>
        public async Task StartPollingAsync(CancellationToken cancellationToken = default)
        {
            var nfcSettings = (NfcReaderSettings)Settings;
            await _transport.StartPollingAsync(nfcSettings.PollingIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stop continuous polling.
        /// </summary>
        public async Task StopPollingAsync(CancellationToken cancellationToken = default)
        {
            await _transport.StopPollingAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send a raw APDU command to the currently presented tag.
        /// </summary>
        public async Task<byte[]?> TransceiveApduAsync(byte[] apdu, CancellationToken cancellationToken = default)
        {
            if (apdu == null)
            {
                throw new ArgumentNullException(nameof(apdu));
            }
            return await _transport.TransceiveAsync(apdu, cancellationToken).ConfigureAwait(false);
        }

        public override void Open()
        {
            _transport.ConnectAsync().GetAwaiter().GetResult();
            _isOpen = true;
        }

        public override void Close()
        {
            _transport.StopPollingAsync().GetAwaiter().GetResult();
            _transport.DisconnectAsync().GetAwaiter().GetResult();
            _isOpen = false;
        }

        public override bool IsOpen()
        {
            return _transport.IsConnected;
        }

        public override void Write(byte[] data)
        {
            // Send raw APDU
            _transport.TransceiveAsync(data).GetAwaiter().GetResult();
        }

        public override byte[] Read(int size)
        {
            if (ReadData.Count >= size)
            {
                return ReadData.GetRange(0, size).ToArray();
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

        private void HandleTagDetected(object? sender, NfcTagData tag)
        {
            ApplyProtocols(tag);
            OnTagDetected?.Invoke(this, tag);

            // Store UID bytes in ReadData for low-level consumers
            ReadData.AddRange(tag.UidBytes);
            InvokeProcessData();
        }

        private void HandleTagRemoved(object? sender, EventArgs e)
        {
            OnTagRemoved?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyProtocols(NfcTagData tag)
        {
            foreach (var protocol in _protocols)
            {
                if (protocol.CanHandle(tag))
                {
                    protocol.Parse(tag, tag.Payload ?? Array.Empty<byte>());
                }
            }
        }
    }
}
