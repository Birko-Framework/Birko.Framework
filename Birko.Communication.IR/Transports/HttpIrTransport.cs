using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Transports
{
    /// <summary>
    /// IR transport via ESPHome REST API.
    /// Sends raw IR commands to an ESP32/ESP8266 running ESPHome with a remote_transmitter component.
    /// API endpoint: POST {baseUrl}/api/services/remote_transmitter/transmit_raw
    /// </summary>
    public class HttpIrTransport : IIrTransport
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string? _apiPassword;
        private bool _isConnected;
        private readonly bool _ownsHttpClient;

        public string Name => "HTTP";
        public bool IsConnected => _isConnected;

#pragma warning disable CS0067 // Event is never used (receive not supported via HTTP)
        public event EventHandler<IrTiming>? OnReceived;
#pragma warning restore CS0067

        /// <summary>
        /// Create an HTTP IR transport targeting an ESPHome device.
        /// </summary>
        /// <param name="baseUrl">ESPHome base URL (e.g., "http://192.168.1.100").</param>
        /// <param name="apiPassword">Optional API password for ESPHome native API.</param>
        public HttpIrTransport(string baseUrl, string? apiPassword = null)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
            _apiPassword = apiPassword;
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }

        public HttpIrTransport(string baseUrl, HttpClient httpClient, string? apiPassword = null)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiPassword = apiPassword;
            _ownsHttpClient = false;
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            _isConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            _isConnected = false;
            return Task.CompletedTask;
        }

        public async Task TransmitAsync(IrTiming timing, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                throw new InvalidOperationException("Transport is not connected.");
            }

            // ESPHome remote_transmitter service expects JSON:
            // { "command": [ mark, -space, mark, -space, ... ] }
            // Where positive = mark (μs), negative = space (μs)
            var signedDurations = new int[timing.Durations.Length];
            for (int i = 0; i < timing.Durations.Length; i++)
            {
                signedDurations[i] = (i % 2 == 0) ? timing.Durations[i] : -timing.Durations[i];
            }

            var payload = new
            {
                command = signedDurations,
                carrier_frequency = timing.CarrierFrequencyHz,
                repeat = timing.RepeatCount
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/services/remote_transmitter/transmit_raw")
            {
                Content = content
            };

            if (!string.IsNullOrEmpty(_apiPassword))
            {
                request.Headers.Add("Authorization", $"Bearer {_apiPassword}");
            }

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        public Task StartReceiveAsync(CancellationToken cancellationToken = default)
        {
            // ESPHome remote_receiver can publish via MQTT or webhook.
            // HTTP polling for received signals is not natively supported.
            // Use MqttIrTransport for learning mode with ESPHome.
            throw new NotSupportedException(
                "HTTP transport does not support receive/learning mode. Use MQTT transport with ESPHome remote_receiver.");
        }

        public Task StopReceiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}
