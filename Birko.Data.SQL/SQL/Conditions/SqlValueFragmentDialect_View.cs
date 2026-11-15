namespace Birko.Data.SQL.Conditions
{
    /// <summary>
    /// Persistent-view SQL, per provider (TASK-917). Lives on the dialect because every consumer loads the provider's base
    /// package (which supplies the connector's dialect) while none loads the provider View package; the base view code in
    /// Birko.Data.SQL.View renders through these, so <c>CreateView</c> and <c>ViewExists</c> work without the View package.
    /// </summary>
    public partial class SqlValueFragmentDialect
    {
        /// <summary>
        /// The statement that creates a view or replaces an existing one. Default <c>CREATE OR REPLACE VIEW</c>
        /// (PostgreSQL, MySQL); SQL Server and SQLite reject that and override it.
        /// </summary>
        /// <param name="quotedViewName">The view name, already quoted by the connector.</param>
        /// <param name="selectSql">The view body.</param>
        public virtual string CreateOrReplaceViewSql(string quotedViewName, string selectSql)
            => "CREATE OR REPLACE VIEW " + quotedViewName + " AS " + selectSql;

        /// <summary>
        /// A catalog query returning a row when a VIEW (not a table) named <c>@viewName</c> exists, and no row otherwise.
        /// A catalog query, not a SELECT from the name: selecting answered "exists" for a table of that name, and its
        /// missing-relation error, swallowed as "no view", aborted an enclosing PostgreSQL transaction (25P02).
        /// </summary>
        public virtual string ViewExistsSql
            => "SELECT 1 FROM information_schema.views WHERE table_name = @viewName";
    }
}
