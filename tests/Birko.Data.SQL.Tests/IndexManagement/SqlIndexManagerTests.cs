using Birko.Data.Patterns.IndexManagement;
using Birko.Data.SQL.IndexManagement;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.SQL.Tests.IndexManagement
{
    public class SqlIndexManagerTests
    {
        [Fact]
        public void Constructor_NullConnector_ThrowsArgumentNullException()
        {
            var act = () => new SqlIndexManager(null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("connector");
        }

        [Fact]
        public async Task ExistsAsync_NullScope_ThrowsArgumentException()
        {
            var manager = new SqlIndexManager(new TestConnector());
            var act = () => manager.ExistsAsync("idx_test", null);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task ExistsAsync_EmptyScope_ThrowsArgumentException()
        {
            var manager = new SqlIndexManager(new TestConnector());
            var act = () => manager.ExistsAsync("idx_test", "");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task ExistsAsync_NullIndexName_ThrowsArgumentException()
        {
            var manager = new SqlIndexManager(new TestConnector());
            var act = () => manager.ExistsAsync(null!, "MyTable");
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public async Task CreateAsync_NullDefinition_ThrowsArgumentNullException()
        {
            var manager = new SqlIndexManager(new TestConnector());
            var act = () => manager.CreateAsync(null!, "MyTable");
            await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("definition");
        }

        [Fact]
        public async Task CreateAsync_EmptyName_ThrowsArgumentException()
        {
            var manager = new SqlIndexManager(new TestConnector());
            var def = new IndexDefinition { Name = "", Fields = new[] { IndexField.Ascending("col1") } };
            var act = () => manager.CreateAsync(def, "MyTable");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task CreateAsync_NoFields_ThrowsArgumentException()
        {
            var manager = new SqlIndexManager(new TestConnector());
            var def = new IndexDefinition { Name = "idx_test" };
            var act = () => manager.CreateAsync(def, "MyTable");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task DropAsync_NullIndexName_ThrowsArgumentException()
        {
            var manager = new SqlIndexManager(new TestConnector());
            var act = () => manager.DropAsync(null!, "MyTable");
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public async Task DropAsync_NullScope_ThrowsArgumentException()
        {
            var manager = new SqlIndexManager(new TestConnector());
            var act = () => manager.DropAsync("idx_test", null);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task ListAsync_NullScope_ThrowsArgumentException()
        {
            var manager = new SqlIndexManager(new TestConnector());
            var act = () => manager.ListAsync(null);
            await act.Should().ThrowAsync<ArgumentException>();
        }
    }

    /// <summary>
    /// Minimal test connector (cannot execute real SQL, only used for validation tests).
    /// </summary>
    internal class TestConnector : Birko.Data.SQL.Connectors.AbstractConnector
    {
        public TestConnector() : base(new TestSettings())
        {
        }

        public override System.Data.Common.DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
        {
            throw new NotSupportedException("Test connector does not support real connections.");
        }

        public override string ConvertType(System.Data.DbType type, SQL.Fields.AbstractField field)
        {
            return type.ToString();
        }

        public override string FieldDefinition(SQL.Fields.AbstractField field)
        {
            return field.Name ?? "";
        }
    }

    internal class TestSettings : Birko.Configuration.PasswordSettings
    {
    }
}
