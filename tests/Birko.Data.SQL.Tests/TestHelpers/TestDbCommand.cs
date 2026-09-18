using System.Data;
using System.Data.Common;

namespace Birko.Data.SQL.Tests.TestHelpers
{
    /// <summary>
    /// Concrete DbCommand stub for testing. Required because DbCommand.Parameters
    /// is non-virtual in .NET 10+ and cannot be mocked with Moq.
    /// </summary>
    public class TestDbCommand : DbCommand
    {
        private readonly TestDbParameterCollection _parameters = new();

        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; } = 30;
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters;

        public TestDbParameterCollection TestParameters => _parameters;

        protected override DbParameter CreateDbParameter() => new TestDbParameter();
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => null;
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotImplementedException();
        public override void Cancel() { }
        public override void Prepare() { }
    }
}
