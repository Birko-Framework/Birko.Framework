namespace Birko.Data.SQL.Connectors
{
    public partial class SqLiteConnector
    {
        // CREATE OR REPLACE VIEW and the view-exists probe come from the connector's dialect (TASK-917), so they
        // work without this package. SQLite needs nothing more for views; the class stays as the package's anchor.
    }
}
