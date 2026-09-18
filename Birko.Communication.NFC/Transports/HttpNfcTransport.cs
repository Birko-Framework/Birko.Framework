using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.NFC.Models;

namespace Birko.Communication.NFC.Transports
{
    /// <summary>
    /// NFC reader transport over HTTP REST API.
    /// For network-attached readers or IoT bridges (ESP32, Raspberry Pi) that expose a REST API.
    ///
    /// Expected API endpoints:
    /// GET  /api/nfc/tag     — Read current tag (returns NfcTagData JSON or 204 if no tag)
    /// POST /api/nfc/apdu    — Send APDU command (body: hex string, returns response hex)
    /// GET  /api/nfc/status  — Reader status (connected, polling state)
    /// </summary>
    public class HttpNfcTransport : INfcTransport
    {
        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private readonly bool _ownsClient;
        private CancellationTokenSource? _pollCts;
        private Task? _pollTask;
        private bool _disposed;
        private string? _lastUid;

        public string Name => "HTTP";
        public bool IsConnected { get; private set; }

        public event EventHandler<NfcTagData>? TagDetected;
        public event EventHandler? TagRemoved;
        public event EventHandler<Exception>? PollingError;

        /// <summary>
        /// Creates an HTTP NFC transport.
        /// </summary>
        /// <param name="baseUrl">Reader API base URL (e.g., "http://192.168.1.100:8080").</param>
        /// <param name="client">Optional HttpClient instance. If null, a new one is created.</param>
        public HttpNfcTransport(string baseUrl, HttpClient? client = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("Base URL cannot be empty.", nameof(baseUrl));
            }
            _baseUrl = baseUrl.TrimEnd('/');
            _ownsClient = client == null;
            _client = client ?? new HttpClient();
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            // Verify reader is reachable
            var response = await _client.GetAsync($"{_baseUrl}/api/nfc/status", cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            IsConnected = true;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            _pollCts?.Cancel();
            IsConnected = false;
            return Task.CompletedTask;
        }

        public async Task<NfcTagData?> ReadTagAsync(int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Transport is not connected.");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            try
            {
                var response = await _client.GetAsync($"{_baseUrl}/api/nfc/tag", cts.Token).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return null;
                }
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                return JsonSerializer.Deserialize<NfcTagData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
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

            // Tracked (so StopPollingAsync can await it). A transient HttpRequestException keeps
            // polling; any other fault (e.g. JsonException, a non-success EnsureSuccessStatusCode)
            // surfaces via PollingError and stops the loop instead of silently faulting (CR-M056).
            _pollTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
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
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (HttpRequestException)
                    {
                        // Reader temporarily unreachable — continue polling
                    }
                    catch (Exception ex)
                    {
                        PollingError?.Invoke(this, ex);
                        break;
                    }

                    try
                    {
                        await Task.Delay(intervalMs, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, token);

            await Task.CompletedTask;
        }

        public async Task StopPollingAsync(CancellationToken cancellationToken = default)
        {
            _pollCts?.Cancel();

            var task = _pollTask;
            if (task != null)
            {
                try { await task.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }

            _pollTask = null;
            _pollCts = null;
            _lastUid = null;
        }

        public async Task<byte[]?> TransceiveAsync(byte[] apdu, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Transport is not connected.");
            }

            var hex = BitConverter.ToString(apdu).Replace("-", "");
            var content = new StringContent($"\"{hex}\"", System.Text.Encoding.UTF8, "application/json");
            var response = await _client.PostAsync($"{_baseUrl}/api/nfc/apdu", content, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();

            var responseHex = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            responseHex = responseHex.Trim('"');
            return Convert.FromHexString(responseHex);
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
            if (_ownsClient)
            {
                _client.Dispose();
            }
        }
    }
}
