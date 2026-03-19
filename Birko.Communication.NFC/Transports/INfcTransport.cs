using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.NFC.Models;

namespace Birko.Communication.NFC.Transports
{
    /// <summary>
    /// Pluggable backend for communicating with NFC/RFID readers.
    /// Implementations wrap different hardware interfaces (serial, HTTP API, HID keyboard).
    /// </summary>
    public interface INfcTransport : IDisposable
    {
        /// <summary>
        /// Transport name for diagnostics (e.g., "Serial", "HTTP", "HID").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether the transport is currently connected/ready.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Open/connect to the NFC reader.
        /// </summary>
        Task ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Close/disconnect from the NFC reader.
        /// </summary>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Read a single tag. Blocks until a tag is presented or timeout expires.
        /// Returns null if no tag was detected within the timeout.
        /// </summary>
        Task<NfcTagData?> ReadTagAsync(int timeoutMs, CancellationToken cancellationToken = default);

        /// <summary>
        /// Start continuous polling for tags.
        /// Detected tags are delivered via the <see cref="TagDetected"/> event.
        /// </summary>
        Task StartPollingAsync(int intervalMs, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stop continuous polling.
        /// </summary>
        Task StopPollingAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Send a raw APDU command to the tag on the reader.
        /// Returns the response APDU bytes, or null if no tag is present.
        /// </summary>
        Task<byte[]?> TransceiveAsync(byte[] apdu, CancellationToken cancellationToken = default);

        /// <summary>
        /// Raised when a tag is detected during polling.
        /// </summary>
        event EventHandler<NfcTagData>? TagDetected;

        /// <summary>
        /// Raised when a previously detected tag is removed from the reader field.
        /// </summary>
        event EventHandler? TagRemoved;
    }
}
