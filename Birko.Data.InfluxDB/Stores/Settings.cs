using System;

namespace Birko.Data.InfluxDB.Stores
{
    /// <summary>
    /// InfluxDB-specific settings for database connection.
    /// </summary>
    public class Settings : Data.Stores.Settings, Data.Models.ILoadable<Settings>
    {
        /// <summary>
        /// Gets or sets the authentication token for InfluxDB.
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// Gets or sets the organization name.
        /// </summary>
        public string Organization { get; set; }

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
        public Settings(string location, string bucket, string token = null, string organization = null)
            : base(location, bucket)
        {
            Token = token;
            Organization = organization;
        }

        /// <summary>
        /// Gets the InfluxDB connection string (server URL).
        /// </summary>
        /// <returns>The server URL.</returns>
        public virtual string GetConnectionString()
        {
            return Location;
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
