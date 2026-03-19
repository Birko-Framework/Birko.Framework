using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Transports
{
    /// <summary>
    /// IR transport via Linux LIRC device (/dev/lirc0) for Raspberry Pi direct GPIO.
    /// Stub — requires Linux LIRC userspace API or /sys/class/rc/ interface.
    /// </summary>
    public class GpioIrTransport : IIrTransport
    {
        private readonly string _devicePath;

        public string Name => "GPIO";
        public bool IsConnected => false;

        public event EventHandler<IrTiming>? OnReceived;

        /// <param name="devicePath">LIRC device path (default "/dev/lirc0").</param>
        public GpioIrTransport(string devicePath = "/dev/lirc0")
        {
            _devicePath = devicePath;
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "GPIO IR transport is a stub. Implement with LIRC userspace API on Linux.");
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "GPIO IR transport is a stub. Implement with LIRC userspace API on Linux.");
        }

        public Task TransmitAsync(IrTiming timing, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "GPIO IR transport is a stub. Implement with LIRC userspace API on Linux.");
        }

        public Task StartReceiveAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "GPIO IR transport is a stub. Implement with LIRC userspace API on Linux.");
        }

        public Task StopReceiveAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException(
                "GPIO IR transport is a stub. Implement with LIRC userspace API on Linux.");
        }

        public void Dispose()
        {
        }
    }
}
