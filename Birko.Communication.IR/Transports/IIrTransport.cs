using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Transports
{
    /// <summary>
    /// Pluggable backend for sending/receiving raw IR signals.
    /// Implementations wrap different hardware interfaces (serial, HTTP, MQTT, GPIO).
    /// </summary>
    public interface IIrTransport : IDisposable
    {
        /// <summary>
        /// Transport name for diagnostics.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether the transport is currently connected/ready.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Open/connect the transport.
        /// </summary>
        Task ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Close/disconnect the transport.
        /// </summary>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Transmit raw IR timings.
        /// </summary>
        Task TransmitAsync(IrTiming timing, CancellationToken cancellationToken = default);

        /// <summary>
        /// Start listening for incoming IR signals (learning mode).
        /// Received signals are delivered via the <see cref="OnReceived"/> event.
        /// </summary>
        Task StartReceiveAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stop listening for incoming IR signals.
        /// </summary>
        Task StopReceiveAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Raised when an IR signal is received (learning mode).
        /// </summary>
        event EventHandler<IrTiming>? OnReceived;
    }
}
