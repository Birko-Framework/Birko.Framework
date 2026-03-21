using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.InfluxDB.Stores
{
    /// <summary>
    /// InfluxDB-specific settings for database connection.
    /// </summary>
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
            // InfluxDB API errors with 429 (too many requests) or 503 (service unavailable)
            if (ex.Message.Contains("429") || ex.Message.Contains("503") || ex.Message.Contains("unavailable")) return true;
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
            }
        }
    }
}
