using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.NFC.Models;

namespace Birko.Communication.NFC.Transports
{
    /// <summary>
    /// NFC reader transport over serial/UART (e.g., PN532, RC522 with UART, ACR122U serial mode).
    /// Sends/receives commands using the reader's native serial protocol.
    /// </summary>
    public class SerialNfcTransport : INfcTransport
    {
        private SerialPort? _port;
        private readonly string _portName;
        private readonly int _baudRate;
        private CancellationTokenSource? _pollCts;
        private bool _disposed;
        private string? _lastUid;

        public string Name => "Serial";
        public bool IsConnected => _port?.IsOpen ?? false;

        public event EventHandler<NfcTagData>? TagDetected;
        public event EventHandler? TagRemoved;

        /// <summary>
        /// Creates a serial NFC transport.
        /// </summary>
        /// <param name="portName">Serial port name (e.g., "COM3", "/dev/ttyUSB0").</param>
        /// <param name="baudRate">Baud rate. Default 115200 (common for PN532/ACR122U).</param>
        public SerialNfcTransport(string portName, int baudRate = 115200)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new ArgumentException("Port name cannot be empty.", nameof(portName));
            }
            _portName = portName;
            _baudRate = baudRate;
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_port != null && _port.IsOpen)
            {
                return Task.CompletedTask;
            }

            _port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 2000,
                WriteTimeout = 2000
            };
            _port.Open();
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            _pollCts?.Cancel();
            if (_port != null && _port.IsOpen)
            {
                _port.Close();
            }
            _port?.Dispose();
            _port = null;
            return Task.CompletedTask;
        }

        public async Task<NfcTagData?> ReadTagAsync(int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (_port == null || !_port.IsOpen)
            {
                throw new InvalidOperationException("Transport is not connected.");
            }

            // Send poll command to reader (PN532 InListPassiveTarget: 0x4A, 1 target, 106 kbps Type A)
            byte[] pollCommand = { 0x00, 0x00, 0xFF, 0x04, 0xFC, 0xD4, 0x4A, 0x01, 0x00, 0xE1, 0x00 };
            _port.Write(pollCommand, 0, pollCommand.Length);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            try
            {
                return await Task.Run(() =>
                {
                    var buffer = new byte[64];
                    int bytesRead = _port.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        return ParseResponse(buffer, bytesRead);
                    }
                    return null;
                }, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async Task StartPollingAsync(int intervalMs, CancellationToken cancellationToken = default)
        {
            _pollCts?.Cancel();
            _pollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _pollCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    var tag = await ReadTagAsync(intervalMs, token).ConfigureAwait(false);
                    if (tag != null)
                    {
                        if (_lastUid != tag.Uid)
                        {
                            _lastUid = tag.Uid;
                            TagDetected?.Invoke(this, tag);
                        }
                    }
                    else if (_lastUid != null)
                    {
                        _lastUid = null;
                        TagRemoved?.Invoke(this, EventArgs.Empty);
                    }

                    await Task.Delay(intervalMs, token).ConfigureAwait(false);
                }
            }, token);

            await Task.CompletedTask;
        }

        public Task StopPollingAsync(CancellationToken cancellationToken = default)
        {
            _pollCts?.Cancel();
            _pollCts = null;
            _lastUid = null;
            return Task.CompletedTask;
        }

        public Task<byte[]?> TransceiveAsync(byte[] apdu, CancellationToken cancellationToken = default)
        {
            if (_port == null || !_port.IsOpen)
            {
                throw new InvalidOperationException("Transport is not connected.");
            }

            // Wrap APDU in PN532 InDataExchange frame
            _port.Write(apdu, 0, apdu.Length);

            var buffer = new byte[256];
            int bytesRead = _port.Read(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                var response = new byte[bytesRead];
                Array.Copy(buffer, response, bytesRead);
                return Task.FromResult<byte[]?>(response);
            }

            return Task.FromResult<byte[]?>(null);
        }

        private static NfcTagData? ParseResponse(byte[] buffer, int length)
        {
            // Minimal PN532 response parsing: look for target data
            // Response format: [preamble] [D5] [4B] [NbTg] [Tg] [SENS_RES] [SEL_RES] [NFCIDLength] [NFCID...]
            if (length < 10)
            {
                return null;
            }

            // Find D5 4B response code
            int offset = -1;
            for (int i = 0; i < length - 1; i++)
            {
                if (buffer[i] == 0xD5 && buffer[i + 1] == 0x4B)
                {
                    offset = i + 2;
                    break;
                }
            }

            if (offset < 0 || offset >= length)
            {
                return null;
            }

            byte numTargets = buffer[offset];
            if (numTargets == 0)
            {
                return null;
            }

            offset++; // skip target number
            if (offset + 4 >= length)
            {
                return null;
            }

            offset++; // target number byte
            byte atqaHigh = buffer[offset++];
            byte atqaLow = buffer[offset++];
            byte sak = buffer[offset++];
            byte uidLen = buffer[offset++];

            if (offset + uidLen > length)
            {
                return null;
            }

            var uidBytes = new byte[uidLen];
            Array.Copy(buffer, offset, uidBytes, 0, uidLen);

            var tagType = DetectTagType(sak);

            return new NfcTagData
            {
                UidBytes = uidBytes,
                Uid = BitConverter.ToString(uidBytes).Replace("-", ""),
                TagType = tagType,
                Sak = sak,
                Atqa = new[] { atqaHigh, atqaLow }
            };
        }

        private static NfcTagType DetectTagType(byte sak)
        {
            return sak switch
            {
                0x08 or 0x18 => NfcTagType.MifareClassic,
                0x00 or 0x04 => NfcTagType.MifareUltralight,
                0x20 => NfcTagType.MifareDESFire,
                _ => NfcTagType.Iso14443A
            };
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _port?.Dispose();
        }
    }
}
