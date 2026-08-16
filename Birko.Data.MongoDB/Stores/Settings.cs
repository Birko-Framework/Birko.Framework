using System;
using System.Collections.Generic;

namespace Birko.Data.MongoDB.Stores
{
    /// <summary>
    /// MongoDB-specific settings for database connection.
    /// Extends RemoteSettings — inherits Location (host), Port, UserName, Password, UseSecure from the framework hierarchy.
    /// </summary>
    public class Settings : Birko.Configuration.RemoteSettings, Data.Models.ILoadable<Settings>
    {
        /// <summary>
        /// Gets or sets the authentication database name (default: admin).
        /// </summary>
        public string AuthDatabase { get; set; } = "admin";

        /// <summary>
        /// Gets or sets the replica set name for replica set connections.
        /// Optional — null/empty means no replica set is included in the connection string.
        /// </summary>
        public string? ReplicaSet { get; set; }

        /// <summary>
        /// Gets or sets a raw MongoDB connection string.
        /// If set, <see cref="GetConnectionString"/> returns this verbatim, ignoring other properties.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The composed form below emits a <b>fixed</b> set of query parameters — <c>authSource</c>,
        /// <c>replicaSet</c>, <c>tls</c>, <c>retryWrites</c>, <c>retryReads</c> — and nothing else can be
        /// added. Everything a real deployment eventually needs is therefore unreachable:
        /// <c>maxPoolSize</c>, <c>appName</c>, <c>connectTimeoutMS</c>, <c>serverSelectionTimeoutMS</c>,
        /// <c>readPreference</c>, write concern, <c>directConnection</c>, and the SOCKS
        /// <c>proxyHost</c>/<c>proxyPort</c> pair — MongoDB's nearest equivalent to the CosmosDB Gateway
        /// mode added in TASK-223. The only workaround was to subclass this type and override
        /// <see cref="GetConnectionString"/>, which the framework's own live probes had to do three times
        /// in one session (TASK-214, TASK-219) merely to set a timeout.
        /// </para>
        /// <para>
        /// Deliberately identical in shape to <c>Birko.Redis.RedisSettings.RawConnectionString</c> — one
        /// answer for the family rather than two — including that only a <b>non-empty</b> value overrides.
        /// An explicit <c>""</c> falls through to the composed form; returning it verbatim would yield an
        /// invalid connection string (the same correction Redis needed in CR-L331).
        /// </para>
        /// </remarks>
        public string? RawConnectionString { get; set; }

        /// <summary>
        /// Initializes a new instance of the Settings class.
        /// </summary>
        public Settings() : base()
        {
            Port = 27017;
        }

        /// <summary>
        /// Initializes a new instance of the Settings class.
        /// </summary>
        /// <param name="location">The server location/hostname.</param>
        /// <param name="name">The database name.</param>
        /// <param name="username">The username for authentication.</param>
        /// <param name="password">The password for authentication.</param>
        public Settings(string location, string name, string? username = null, string? password = null)
            : base(location, name, username ?? string.Empty, password ?? string.Empty, 27017)
        {
        }

        /// <summary>
        /// Gets the MongoDB connection string based on the current settings.
        /// </summary>
        /// <returns>A MongoDB connection string.</returns>
        public virtual string GetConnectionString()
        {
            // Only an actually-set raw string overrides; an explicit "" falls through to the composed
            // form. Mirrors RedisSettings, including that correction (CR-L331).
            if (!string.IsNullOrEmpty(RawConnectionString))
            {
                return RawConnectionString;
            }

            var connectionString = "mongodb://";

            // Add credentials if provided
            if (!string.IsNullOrEmpty(UserName) && !string.IsNullOrEmpty(Password))
            {
                connectionString += $"{UserName}:{Password}@";
            }

            // Add server and port
            connectionString += $"{Location}:{Port}";

            // Add database name
            if (!string.IsNullOrEmpty(Name))
            {
                connectionString += $"/{Name}";
            }

            // Add query parameters
            var queryParams = new List<string>();

            if (!string.IsNullOrEmpty(AuthDatabase))
            {
                queryParams.Add($"authSource={AuthDatabase}");
            }

            if (!string.IsNullOrEmpty(ReplicaSet))
            {
                queryParams.Add($"replicaSet={ReplicaSet}");
            }

            if (UseSecure)
            {
                queryParams.Add("tls=true");
            }

            // Enable retry for transient failures (MongoDB 3.6+)
            queryParams.Add("retryWrites=true");
            queryParams.Add("retryReads=true");

            if (queryParams.Count > 0)
            {
                connectionString += "?" + string.Join("&", queryParams);
            }

            return connectionString;
        }

        /// <inheritdoc />
        public override string GetId()
        {
            return $"{Location}:{Port}:{Name}:{UserName}";
        }

        /// <summary>
        /// Loads settings from another Settings instance.
        /// </summary>
        /// <param name="data">The settings to load from.</param>
        public void LoadFrom(Settings data)
        {
            if (data != null)
            {
                base.LoadFrom((Birko.Configuration.RemoteSettings)data);
                AuthDatabase = data.AuthDatabase;
                ReplicaSet = data.ReplicaSet;
                RawConnectionString = data.RawConnectionString;
            }
        }

        public override void LoadFrom(Birko.Configuration.Settings data)
        {
            if (data is Settings mongoData)
            {
                LoadFrom(mongoData);
            }
            else
            {
                base.LoadFrom(data);
            }
        }
    }
}
