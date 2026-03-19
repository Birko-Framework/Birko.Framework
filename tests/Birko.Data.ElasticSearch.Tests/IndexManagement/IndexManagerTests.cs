using Birko.Data.ElasticSearch.IndexManagement;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests.IndexManagement
{
    public class IndexManagerTests
    {
        [Fact]
        public void Constructor_NullClient_ThrowsArgumentNullException()
        {
            var act = () => new IndexManager(null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("client");
        }

        [Fact]
        public void IndexExists_NullIndexName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.IndexExists(null!);
            act.Should().Throw<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void IndexExists_EmptyIndexName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.IndexExists("");
            act.Should().Throw<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void IndexExists_WhitespaceIndexName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.IndexExists("   ");
            act.Should().Throw<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void CreateIndex_NullIndexName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.CreateIndex(null!);
            act.Should().Throw<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void DeleteIndex_NullIndexName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.DeleteIndex(null!);
            act.Should().Throw<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void OpenIndex_NullIndexName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.OpenIndex(null!);
            act.Should().Throw<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void CloseIndex_NullIndexName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.CloseIndex(null!);
            act.Should().Throw<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void UpdateSettings_NullIndexName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.UpdateSettings(null!, s => s);
            act.Should().Throw<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void UpdateSettings_NullDescriptor_ThrowsArgumentNullException()
        {
            var manager = CreateManager();
            var act = () => manager.UpdateSettings("test", null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("settingsDescriptor");
        }

        [Fact]
        public void GetIndexInfo_NullIndexName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.GetIndexInfo(null!);
            act.Should().Throw<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void UpdateMapping_NullIndexName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.UpdateMapping<object>(null!, m => m);
            act.Should().Throw<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void UpdateMapping_NullDescriptor_ThrowsArgumentNullException()
        {
            var manager = CreateManager();
            var act = () => manager.UpdateMapping<object>("test", null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("mappingDescriptor");
        }

        [Fact]
        public void CreateAlias_NullIndexName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.CreateAlias(null!, "alias");
            act.Should().Throw<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void CreateAlias_NullAliasName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.CreateAlias("index", null!);
            act.Should().Throw<ArgumentException>().WithParameterName("aliasName");
        }

        [Fact]
        public void DeleteAlias_NullIndexName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.DeleteAlias(null!, "alias");
            act.Should().Throw<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void DeleteAlias_EmptyAliasName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.DeleteAlias("index", "");
            act.Should().Throw<ArgumentException>().WithParameterName("aliasName");
        }

        [Fact]
        public void SwapAlias_NullAliasName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.SwapAlias(null!, "old", "new");
            act.Should().Throw<ArgumentException>().WithParameterName("aliasName");
        }

        [Fact]
        public void SwapAlias_NullOldIndex_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.SwapAlias("alias", null!, "new");
            act.Should().Throw<ArgumentException>().WithParameterName("oldIndexName");
        }

        [Fact]
        public void SwapAlias_NullNewIndex_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.SwapAlias("alias", "old", null!);
            act.Should().Throw<ArgumentException>().WithParameterName("newIndexName");
        }

        [Fact]
        public void PutTemplate_NullName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.PutTemplate(null!, t => t);
            act.Should().Throw<ArgumentException>().WithParameterName("templateName");
        }

        [Fact]
        public void PutTemplate_NullDescriptor_ThrowsArgumentNullException()
        {
            var manager = CreateManager();
            var act = () => manager.PutTemplate("test", null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("descriptor");
        }

        [Fact]
        public void DeleteTemplate_NullName_ThrowsArgumentException()
        {
            var manager = CreateManager();
            var act = () => manager.DeleteTemplate(null!);
            act.Should().Throw<ArgumentException>().WithParameterName("templateName");
        }

        private static IndexManager CreateManager()
        {
            // Create a client pointed at a non-existent host for parameter validation tests.
            // These tests only exercise argument checks — no ES calls are made.
            var settings = new Nest.ConnectionSettings(new Uri("http://localhost:19200"))
                .DisableDirectStreaming();
            return new IndexManager(new Nest.ElasticClient(settings));
        }
    }
}
