using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.NFC.Models;

namespace Birko.Communication.NFC.Transports
{
    /// <summary>
    /// NFC reader transport for HID keyboard-emulation readers.
    /// Most enterprise access control readers (ACR122U in HID mode, RF IDEAS, etc.)
    /// send the tag UID as keyboard keystrokes followed by Enter.
    ///
    /// This transport captures the keystroke buffer (typically via stdin or a named pipe)
    /// and parses it as a hex UID.
    /// </summary>
    public class HidNfcTransport : INfcTransport
    {
        private CancellationTokenSource? _pollCts;
        private bool _disposed;
        private string? _lastUid;
        private readonly StringBuilder _buffer = new();
        private readonly SemaphoreSlim _bufferLock = new(1, 1);
        private TaskCompletionSource<string?>? _readTcs;

        public string Name => "HID";
        public bool IsConnected { get; private set; }

        public event EventHandler<NfcTagData>? TagDetected;
        public event EventHandler? TagRemoved;

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            _pollCts?.Cancel();
            IsConnected = false;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Feed keystroke data from the HID reader into this transport.
        /// Call this from your input capture layer (console ReadLine, WinForms KeyPress, etc.).
        /// When a complete UID is received (terminated by Enter/newline), it becomes available
        /// for ReadTagAsync and polling.
        /// </summary>
        public async Task FeedInputAsync(string input)
        {
            await _bufferLock.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (char c in input)
                {
                    if (c == '\r' || c == '\n')
                    {
                        if (_buffer.Length > 0)
                        {
                            var uid = _buffer.ToString().Trim();
                            _buffer.Clear();
                            _readTcs?.TrySetResult(uid);
                        }
                    }
                    else
                    {
                        _buffer.Append(c);
                    }
                }
            }
            finally
            {
                _bufferLock.Release();
            }
        }

        public async Task<NfcTagData?> ReadTagAsync(int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Transport is not connected.");
            }

            var tcs = new TaskCompletionSource<string?>();
            _readTcs = tcs;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            // Register against the LOCAL tcs, not the _readTcs field: the field is nulled below (and
            // reassigned by the next polling iteration), so a late timer callback would otherwise
            // NRE on a null field or complete the WRONG read. Disposing the registration when the
            // method returns also stops the timer from firing against a finished read (CR-H029).
            using var registration = cts.Token.Register(() => tcs.TrySetResult(null));

            var uid = await tcs.Task.ConfigureAwait(false);
            _readTcs = null;

            if (string.IsNullOrEmpty(uid))
            {
                return null;
            }

            return ParseHidUid(uid);
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

        /// <summary>
        /// Not supported for HID readers — they only send UIDs, no APDU capability.
        /// </summary>
        public Task<byte[]?> TransceiveAsync(byte[] apdu, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("HID keyboard-emulation readers do not support APDU commands. Use Serial or HTTP transport for APDU access.");
        }

        private static NfcTagData ParseHidUid(string uid)
        {
            // HID readers typically output hex UID, sometimes decimal
            // Try hex first, fall back to treating as decimal card number
            uid = uid.Replace(":", "").Replace("-", "").Replace(" ", "").ToUpperInvariant();

            byte[] uidBytes;
            try
            {
                uidBytes = Convert.FromHexString(uid);
            }
            catch (FormatException)
            {
                // Decimal card number — convert to bytes
                if (ulong.TryParse(uid, out var cardNumber))
                {
                    uidBytes = BitConverter.GetBytes(cardNumber);
                    // Trim trailing zeros
                    int len = 8;
                    while (len > 1 && uidBytes[len - 1] == 0) len--;
                    Array.Resize(ref uidBytes, len);
                    uid = BitConverter.ToString(uidBytes).Replace("-", "");
                }
                else
                {
                    uidBytes = Encoding.ASCII.GetBytes(uid);
                }
            }

            return new NfcTagData
            {
                Uid = uid,
                UidBytes = uidBytes,
                TagType = NfcTagType.Unknown // HID readers don't report tag type
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
            _bufferLock.Dispose();
        }
    }
}
