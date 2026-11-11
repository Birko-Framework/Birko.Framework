using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.InfluxDB.Stores
{
    /// <summary>
    /// InfluxDB-specific settings for database connection.
    /// </summary>
    /// <remarks>
    /// CR-L121: this intentionally extends <see cref="Birko.Configuration.Settings"/> directly rather than
    /// the RemoteSettings chain that user/password/port backends (CosmosDB/RavenDB) descend. InfluxDB v2
    /// authenticates with a token + organization, not a username/password/port, so RemoteSettings' credential
    /// slots (UserName/Password/Port/UseSecure) do not apply — the server URL is carried by the base
    /// <c>Location</c> and auth is the InfluxDB-specific <see cref="Token"/>/<see cref="Organization"/> pair.
    /// </remarks>
    public class Settings : Birko.Configuration.Settings, Data.Models.ILoadable<Settings>
    {
        /// <summary>
        /// Gets or sets the authentication token for InfluxDB.
        /// </summary>
        public string Token { get; set; } = null!;

        /// <summary>
        /// Gets or sets the organization name.
        /// </summary>
        public string Organization { get; set; } = null!;

        /// <summary>
        /// Gets or sets the bucket name.
        /// This is also mapped to the base Settings.Name property.
        /// </summary>
        public string Bucket
        {
            get => Name;
            set => Name = value;
        }

        /// <summary>
        /// Initializes a new instance of the Settings class.
        /// </summary>
        public Settings() : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the Settings class.
        /// </summary>
        /// <param name="location">The server URL (e.g., "http://localhost:8086").</param>
        /// <param name="bucket">The bucket name.</param>
        /// <param name="token">The authentication token.</param>
        /// <param name="organization">The organization name.</param>
        public Settings(string location, string bucket, string? token = null, string? organization = null)
            : base(location, bucket)
        {
            Token = token ?? string.Empty;
            Organization = organization ?? string.Empty;
        }

        /// <summary>
        /// Retry policy for transient InfluxDB failures (HTTP errors, timeouts).
        /// Default is no retries. Set to RetryPolicy.Default for 3 retries with exponential backoff.
        /// </summary>
        public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.None;

        /// <summary>
        /// The longest a single HTTP request (a read, an aggregate's read, a write) may take before the client abandons it.
        /// <c>null</c> (the default) means "not set here": the <c>timeout</c> the connection string carries
        /// (<c>http://host:8086?timeout=60000</c>, in milliseconds) if it carries one, otherwise the InfluxDB client's own
        /// default of 10 seconds.
        /// </summary>
        /// <remarks>
        /// TASK-908. Every point is its own series (the <c>Guid</c> tag), so a read or aggregate over ~100k points takes
        /// 45–50 s and fails at the 10 s default — measured on InfluxDB 2. The default stays 10 s: a longer one would only
        /// delay every failure for consumers whose requests are small; a consumer reading that much sets it.
        /// <para>
        /// Accepts a positive value up to 4,294,967,294 ms (~49.7 days, the longest the client can schedule — a longer
        /// one fails every request) or <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> for no timeout; anything
        /// else is refused here, since the client would otherwise cancel every request at once (zero) or fail each one
        /// (negative). Setting it while the connection string also carries a <c>timeout</c> is refused when the client is
        /// built, rather than picking one of the two.
        /// </para>
        /// </remarks>
        public TimeSpan? RequestTimeout
        {
            get => _requestTimeout;
            set
            {
                if (value is TimeSpan timeout && timeout != System.Threading.Timeout.InfiniteTimeSpan
                    && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > MaxRequestTimeoutMilliseconds))
                {
                    throw new ArgumentOutOfRangeException(nameof(RequestTimeout), timeout,
                        "InfluxDB RequestTimeout must be positive and at most 4294967294 ms (~49.7 days), "
                        + "or Timeout.InfiniteTimeSpan for no timeout; leave it null for the client's default (10 s).");
                }
                _requestTimeout = value;
            }
        }

        private TimeSpan? _requestTimeout;

        /// <summary>The longest timeout the client can schedule (a <see cref="CancellationTokenSource"/> delay), measured.</summary>
        private const double MaxRequestTimeoutMilliseconds = uint.MaxValue - 1.0;

        /// <summary>
        /// The options the InfluxDB client is built from — the one place a client of a store or repository is configured
        /// (<see cref="InfluxDB.InfluxDBClient"/>). Reads <see cref="GetConnectionString"/> before <see cref="Token"/>, as the
        /// client always has, so an override that derives the token there keeps working.
        /// </summary>
        /// <exception cref="InvalidOperationException"><see cref="RequestTimeout"/> is set and the connection string also
        /// carries a <c>timeout</c>.</exception>
        public virtual global::InfluxDB.Client.InfluxDBClientOptions GetClientOptions()
        {
            var url = GetConnectionString();
            var options = new global::InfluxDB.Client.InfluxDBClientOptions(url) { Token = Token };
            if (RequestTimeout is not TimeSpan timeout)
            {
                return options;
            }

            // The client's own parse of the connection string (case-insensitive key, blank ignored).
            var fromUrl = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query).Get("timeout");
            if (!string.IsNullOrWhiteSpace(fromUrl))
            {
                throw new InvalidOperationException(
                    $"InfluxDB RequestTimeout is set ({timeout}) and the connection string also carries timeout={fromUrl}. "
                    + "Set one of them: remove the timeout from the connection string, or leave RequestTimeout null.");
            }

            options.Timeout = timeout;
            return options;
        }

        /// <summary>
        /// Gets the InfluxDB connection string (server URL).
        /// </summary>
        /// <returns>The server URL.</returns>
        public virtual string GetConnectionString()
        {
            return Location;
        }

        /// <summary>
        /// Determines whether an exception from InfluxDB is transient and should be retried.
        /// </summary>
        public virtual bool IsTransientException(Exception ex)
        {
            if (ex is TimeoutException) return true;
            if (ex is HttpRequestException) return true;
            if (ex is TaskCanceledException tce && tce.InnerException is TimeoutException) return true;
            // InfluxDB API errors with 429 (too many requests) or 503 (service unavailable), by the status the client
            // reports. TASK-724: this read the message text, so any message holding "429", "503" or "unavailable" — a bucket
            // named that, an id — was retried as transient.
            if (ex is global::InfluxDB.Client.Core.Exceptions.InfluxException { Status: 429 or 503 }) return true;
            return false;
        }

        /// <inheritdoc />
        public override string GetId()
        {
            return $"{Location}:{Organization}:{Bucket}";
        }

        /// <summary>
        /// Loads settings from another Settings instance.
        /// </summary>
        /// <param name="data">The settings to load from.</param>
        public void LoadFrom(Settings data)
        {
            if (data != null)
            {
                base.LoadFrom(data);
                Token = data.Token;
                Organization = data.Organization;
                RequestTimeout = data.RequestTimeout;
            }
        }
    }
}
