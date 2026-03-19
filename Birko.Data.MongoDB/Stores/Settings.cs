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
        /// </summary>
        public string ReplicaSet { get; set; } = null!;

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
