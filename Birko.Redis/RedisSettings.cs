using System.Text;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Redis
{
    /// <summary>
    /// Redis connection settings extending the framework's RemoteSettings hierarchy.
    /// Maps inherited fields to Redis concepts: Location=Host, Port=Port, Password=AUTH,
    /// UserName=ACL username, UseSecure=TLS, Name=client name.
    /// </summary>
    public class RedisSettings : RemoteSettings, Birko.Data.Models.ILoadable<RedisSettings>
    {
        #region Properties

        /// <summary>
        /// Gets or sets the Redis database index (0-15). Default: 0.
        /// </summary>
        public int Database { get; set; }

        /// <summary>
        /// Gets or sets the key prefix for namespace isolation.
        /// All keys created by consumers should be prefixed with this value.
        /// </summary>
        public string? KeyPrefix { get; set; }

        /// <summary>
        /// Gets or sets a raw StackExchange.Redis connection string.
        /// If set, GetConnectionString() returns this verbatim, ignoring other properties.
        /// </summary>
        public string? RawConnectionString { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance with default values (localhost:6379).
        /// </summary>
        public RedisSettings() : base()
        {
            Location = "localhost";
            Port = 6379;
        }

        /// <summary>
        /// Initializes a new instance with host and port.
        /// </summary>
        /// <param name="host">The Redis server host.</param>
        /// <param name="port">The Redis server port.</param>
        /// <param name="password">The AUTH password.</param>
        /// <param name="database">The database index (0-15).</param>
        /// <param name="useSsl">Whether to use TLS.</param>
        public RedisSettings(string host, int port = 6379, string? password = null, int database = 0, bool useSsl = false)
            : base(host, null!, null!, password ?? null!, port, useSsl)
        {
            Database = database;
        }

        #endregion

        #region Connection String

        /// <summary>
        /// Builds a StackExchange.Redis connection string from the settings.
        /// If RawConnectionString is set, returns it verbatim.
        /// </summary>
        public string GetConnectionString()
        {
            if (RawConnectionString != null)
            {
                return RawConnectionString;
            }

            var sb = new StringBuilder();
            sb.Append(Location ?? "localhost");
            sb.Append(':');
            sb.Append(Port > 0 ? Port : 6379);

            if (!string.IsNullOrEmpty(Password))
            {
                sb.Append(",password=");
                sb.Append(Password);
            }

            if (!string.IsNullOrEmpty(UserName))
            {
                sb.Append(",user=");
                sb.Append(UserName);
            }

            if (UseSecure)
            {
                sb.Append(",ssl=True,sslHost=");
                sb.Append(Location ?? "localhost");
            }

            if (Database != 0)
            {
                sb.Append(",defaultDatabase=");
                sb.Append(Database);
            }

            if (!string.IsNullOrEmpty(Name))
            {
                sb.Append(",name=");
                sb.Append(Name);
            }

            return sb.ToString();
        }

        #endregion

        #region ISettings Implementation

        /// <inheritdoc />
        public override string GetId()
        {
            return string.Format("{0}:{1}", base.GetId(), Database);
        }

        #endregion

        #region ILoadable Implementation

        /// <summary>
        /// Loads settings from another RedisSettings instance.
        /// </summary>
        /// <param name="data">The settings to load from.</param>
        public void LoadFrom(RedisSettings data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                Database = data.Database;
                KeyPrefix = data.KeyPrefix;
                RawConnectionString = data.RawConnectionString;
            }
        }

        public override void LoadFrom(Settings data)
        {
            if (data is RedisSettings redisData)
            {
                LoadFrom(redisData);
            }
        }

        #endregion
    }
}
