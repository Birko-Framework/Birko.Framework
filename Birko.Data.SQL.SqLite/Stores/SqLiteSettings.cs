using System;
using Birko.Configuration;
using Birko.Data.Models;

namespace Birko.Data.SQL.SqLite.Stores
{
    /// <summary>
    /// SQLite-specific settings.
    /// Extends PasswordSettings (not RemoteSettings/SqlSettings) since SQLite is file-based
    /// and doesn't need username, port, or secure connection options.
    /// </summary>
    public class SqLiteSettings : PasswordSettings, ILoadable<SqLiteSettings>
    {
        /// <summary>
        /// Gets or sets the command timeout in seconds. Default is 30.
        /// </summary>
        public int CommandTimeout { get; set; } = 30;

        /// <summary>
        /// The journal mode this database is put into on first use — <c>"WAL"</c> (the default) or
        /// <c>"DELETE"</c>, or null/empty to leave whatever the file already has entirely alone.
        /// TASK-296.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠ <b>This is a correctness setting before it is a performance one.</b> On the rollback journal
        /// (SQLite's default, and what this framework used to leave in place) a statement on a
        /// <b>pooled</b> <c>sqlite3</c> handle can be answered from a schema image older than a
        /// <c>CREATE TABLE</c> another connection has already committed — so a freshly created table reads
        /// as missing, and because a count of a missing table answers <c>0</c> (TASK-285) it does so
        /// <b>silently</b>. TASK-290 measured that: 7 of 7 storm runs, 2-9 occurrences each. On WAL:
        /// <b>0 of 5</b>.
        /// </para>
        /// <para>
        /// It is also faster on every axis measured, which is not why it was chosen but is worth knowing:
        /// <b>5×</b> in the warm sequential case (351 ms against 1,801 ms for 200 write+count+read cycles)
        /// and <b>20-40×</b> under the storm. The other configuration that fixes the defect,
        /// <c>Pooling=False</c>, is <b>1.5× slower</b> in that same steady state — the storm made it look
        /// free and it is not.
        /// </para>
        /// <para>
        /// ⚠ <b>Two consequences of WAL a consumer must know, which is why this is settable.</b> The mode
        /// is <b>persistent in the database file</b>, so it survives the process and is reversible only by
        /// setting this back; and WAL keeps its recent commits in a <c>-wal</c> sidecar, so <b>a backup
        /// that copies only the <c>.db</c> file can lose them</b>. Copy the whole set, or checkpoint first.
        /// WAL also needs shared memory, so it does not engage on most network filesystems — see
        /// <c>SqLiteConnector.JournalModeInEffect</c>, which reports what actually took.
        /// </para>
        /// <para>
        /// ⚠ <b>Only those two values, and that is measured rather than cautious.</b> SQLite persists a
        /// journal mode in the file only for WAL; TRUNCATE, PERSIST, MEMORY and OFF are per-connection
        /// properties, so setting one here — once, on a connection of its own — would accept the value and
        /// silently do nothing. Anything else is refused and recorded on
        /// <c>SqLiteConnector.JournalModeFailure</c>.
        /// </para>
        /// <para>
        /// ⚠ <b>It is applied per database, by whichever settings object created the connector.</b>
        /// <c>DataBase.GetConnector</c> caches per (type, settings id) and the id is <c>Location:Name</c>
        /// only — so, exactly as with <see cref="CommandTimeout"/>, the first caller's value wins for
        /// everyone on that file.
        /// </para>
        /// </remarks>
        public string? JournalMode { get; set; } = "WAL";

        public SqLiteSettings() : base() { }

        public SqLiteSettings(string location, string name, string? password = null)
            : base(location, name, password ?? string.Empty) { }

        /// <summary>
        /// Gets the database file path derived from Location and Name.
        /// </summary>
        public string? Path => (!string.IsNullOrEmpty(Location) && !string.IsNullOrEmpty(Name))
            ? System.IO.Path.Combine(Location, Name)
            : null;

        /// <summary>
        /// Gets the SQLite connection string from the current settings.
        /// </summary>
        public virtual string GetConnectionString()
        {
            var cs = $"Data Source={Path}";
            if (!string.IsNullOrEmpty(Password))
            {
                cs += $";Password={Password}";
            }
            cs += $";Default Timeout={CommandTimeout}";
            return cs;
        }

        public void LoadFrom(SqLiteSettings data)
        {
            if (data != null)
            {
                base.LoadFrom((PasswordSettings)data);
                CommandTimeout = data.CommandTimeout;
                JournalMode = data.JournalMode;
            }
        }

        public override void LoadFrom(Birko.Configuration.Settings data)
        {
            if (data is SqLiteSettings sqliteData)
            {
                LoadFrom(sqliteData);
            }
            else
            {
                base.LoadFrom(data);
            }
        }
    }
}
