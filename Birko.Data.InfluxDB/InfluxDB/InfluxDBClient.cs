using System;
using InfluxDB.Client;

namespace Birko.Data.InfluxDB
{
    /// <summary>
    /// Wrapper around InfluxDB.Client's InfluxDBClient.
    /// </summary>
    public class InfluxDBClient : IDisposable
    {
        /// <summary>
        /// Gets the underlying InfluxDB client.
        /// </summary>
        public global::InfluxDB.Client.InfluxDBClient Client { get; private set; }

        /// <summary>
        /// Gets the settings used for this connection.
        /// </summary>
        public Stores.Settings Settings { get; private set; }

        /// <summary>
        /// Initializes a new instance of the InfluxDBClient class.
        /// </summary>
        /// <param name="settings">The InfluxDB settings.</param>
        public InfluxDBClient(Stores.Settings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));

            Client = new global::InfluxDB.Client.InfluxDBClient(
                settings.GetConnectionString(),
                settings.Token);
        }

        /// <summary>
        /// Gets the async write API.
        /// </summary>
        /// <returns>The write API for async operations.</returns>
        public WriteApiAsync GetWriteApiAsync()
        {
            return Client.GetWriteApiAsync();
        }

        /// <summary>
        /// Gets the blocking write API.
        /// </summary>
        /// <returns>The write API for synchronous operations.</returns>
        public WriteApi GetWriteApi()
        {
            return Client.GetWriteApi();
        }

        /// <summary>
        /// Gets the query API.
        /// </summary>
        /// <returns>The query API.</returns>
        public QueryApi GetQueryApi()
        {
            return Client.GetQueryApi();
        }

        /// <summary>
        /// Gets the delete API.
        /// </summary>
        /// <returns>The delete API.</returns>
        public DeleteApi GetDeleteApi()
        {
            return Client.GetDeleteApi();
        }

        /// <summary>
        /// Gets the buckets API for managing buckets.
        /// </summary>
        /// <returns>The buckets API.</returns>
        public BucketsApi GetBucketsApi()
        {
            return Client.GetBucketsApi();
        }

        /// <summary>
        /// Gets the organizations API for managing organizations.
        /// </summary>
        /// <returns>The organizations API.</returns>
        public OrganizationsApi GetOrganizationsApi()
        {
            return Client.GetOrganizationsApi();
        }

        /// <summary>
        /// Checks if the InfluxDB server is reachable.
        /// </summary>
        /// <returns>True if the server is reachable, false otherwise.</returns>
        public bool IsHealthy()
        {
            try
            {
                return Client.PingAsync().GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the InfluxDB server is reachable asynchronously.
        /// </summary>
        /// <returns>True if the server is reachable, false otherwise.</returns>
        public async System.Threading.Tasks.Task<bool> IsHealthyAsync()
        {
            try
            {
                return await Client.PingAsync();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Disposes the InfluxDB client.
        /// </summary>
        public void Dispose()
        {
            Client?.Dispose();
        }
    }
}
