using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.IndexManagement;

namespace Birko.Data.SQL.MySQL.IndexManagement
{
    /// <summary>
    /// MySQL dialect for <see cref="SqlIndexManager"/>.
    /// Uses information_schema.statistics (the default base implementation).
    /// </summary>
    public class MySqlIndexManager : SqlIndexManager
    {
        public MySqlIndexManager(AbstractConnectorBase connector) : base(connector)
        {
        }

        // MySQL uses information_schema.statistics — the base implementation is already correct.
        // This class exists for naming consistency and as an extension point.
    }
}
