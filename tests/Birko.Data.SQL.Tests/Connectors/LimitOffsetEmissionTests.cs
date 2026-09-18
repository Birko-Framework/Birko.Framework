using System.Data.Common;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;
using TestConnector = Birko.Data.SQL.Tests.IndexManagement.TestConnector;

namespace Birko.Data.SQL.Tests.Connectors
{
    /// <summary>
    /// TASK-278 — the base limit/offset emission and the capability that gates the synthesised sort.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQL Server needed two changes (its offset defaults to 0, and a sort is synthesised when the caller
    /// gave none) and this suite is the other half of that: proof that the shared path did <b>not</b> change
    /// for the providers that were already correct. SQLite, PostgreSQL and MySQL accept
    /// <c>LIMIT</c>/<c>OFFSET</c> with no sort at all, so <c>RequiresOrderByForPaging</c> must stay false
    /// there — asserted here on the base and in each provider suite, because a capability whose false side
    /// is untested is indistinguishable from an unconditional true.
    /// </para>
    /// </remarks>
    public class LimitOffsetEmissionTests
    {
        /// <summary>
        /// The base emits ANSI <c>LIMIT</c>, and the offset only when the caller supplied one — unchanged by
        /// TASK-278. This is the byte-identical pin for the three providers that inherit it.
        /// </summary>
        [Fact]
        public void The_base_emits_limit_and_only_adds_offset_when_asked()
        {
            var connector = new TestConnector();
            using var command = new NoopCommand();

            connector.LimitOffsetDefinition(command, 5, null).Should().Be(" LIMIT @LIMIT");
            connector.LimitOffsetDefinition(command, 5, 10).Should().Be(" LIMIT @LIMIT OFFSET @OFFSET");
        }

        [Fact]
        public void The_base_emits_nothing_without_a_limit()
        {
            var connector = new TestConnector();
            using var command = new NoopCommand();

            connector.LimitOffsetDefinition(command, null, 10).Should().BeNull(
                "CreateSelectCommand only calls this when a limit is set");
        }

        /// <summary>
        /// False by default — the three providers that take a bare <c>LIMIT</c> must not gain a synthesised
        /// <c>ORDER BY</c>. SQL Server's own suite asserts the true side.
        /// </summary>
        [Fact]
        public void RequiresOrderByForPaging_is_false_by_default()
        {
            new TestConnector().RequiresOrderByForPaging.Should().BeFalse();
        }

        /// <summary>
        /// A command that records nothing and executes nothing — <c>LimitOffsetDefinition</c> only needs a
        /// parameter collection, and the providers' real command types demand a live connection.
        /// </summary>
        private sealed class NoopCommand : DbCommand
        {
            private readonly NoopParameterCollection _parameters = new();

            public override string CommandText { get; set; } = string.Empty;
            public override int CommandTimeout { get; set; }
            public override System.Data.CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override System.Data.UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; }
            protected override DbParameterCollection DbParameterCollection => _parameters;
            protected override DbTransaction? DbTransaction { get; set; }

            public override void Cancel() { }
            public override int ExecuteNonQuery() => 0;
            public override object? ExecuteScalar() => null;
            public override void Prepare() { }
            protected override DbParameter CreateDbParameter() => new NoopParameter();
            protected override DbDataReader ExecuteDbDataReader(System.Data.CommandBehavior behavior)
                => throw new System.NotSupportedException();
        }

        private sealed class NoopParameter : DbParameter
        {
            public override System.Data.DbType DbType { get; set; }
            public override System.Data.ParameterDirection Direction { get; set; }
            public override bool IsNullable { get; set; }
            public override string ParameterName { get; set; } = string.Empty;
            public override int Size { get; set; }
            public override string SourceColumn { get; set; } = string.Empty;
            public override bool SourceColumnNullMapping { get; set; }
            public override object? Value { get; set; }
            public override void ResetDbType() { }
        }

        private sealed class NoopParameterCollection : DbParameterCollection
        {
            private readonly System.Collections.Generic.List<DbParameter> _items = new();

            public override int Count => _items.Count;
            public override object SyncRoot => _items;

            public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
            public override void AddRange(System.Array values) { foreach (var v in values) Add(v!); }
            public override void Clear() => _items.Clear();
            public override bool Contains(object value) => _items.Contains((DbParameter)value);
            public override bool Contains(string value) => IndexOf(value) >= 0;
            public override void CopyTo(System.Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);
            public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
            public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
            public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
            public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
            public override void Remove(object value) => _items.Remove((DbParameter)value);
            public override void RemoveAt(int index) => _items.RemoveAt(index);
            public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));
            protected override DbParameter GetParameter(int index) => _items[index];
            protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
            protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
            protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
        }
    }
}
