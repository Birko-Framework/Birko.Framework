using Birko.Data.ElasticSearch.IndexManagement;
using Birko.Data.Patterns.IndexManagement;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests.IndexManagement
{
    public class ElasticSearchIndexManagerAdapterTests
    {
        [Fact]
        public void Constructor_NullClient_ThrowsArgumentNullException()
        {
            var act = () => new ElasticSearchIndexManagerAdapter(null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("client");
        }

        [Fact]
        public void Native_ReturnsIndexManager()
        {
            var settings = new Nest.ConnectionSettings(new Uri("http://localhost:9200"));
            var client = new Nest.ElasticClient(settings);
            var adapter = new ElasticSearchIndexManagerAdapter(client);

            adapter.Native.Should().NotBeNull();
            adapter.Native.Should().BeOfType<IndexManager>();
        }

        [Fact]
        public async Task ExistsAsync_NullIndexName_ThrowsArgumentException()
        {
            var adapter = CreateAdapter();
            var act = () => adapter.ExistsAsync(null!);
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public async Task ExistsAsync_EmptyIndexName_ThrowsArgumentException()
        {
            var adapter = CreateAdapter();
            var act = () => adapter.ExistsAsync("");
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public async Task CreateAsync_NullDefinition_ThrowsArgumentNullException()
        {
            var adapter = CreateAdapter();
            var act = () => adapter.CreateAsync(null!);
            await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("definition");
        }

        [Fact]
        public async Task CreateAsync_EmptyName_ThrowsArgumentException()
        {
            var adapter = CreateAdapter();
            var def = new IndexDefinition { Name = "" };
            var act = () => adapter.CreateAsync(def);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task DropAsync_NullIndexName_ThrowsArgumentException()
        {
            var adapter = CreateAdapter();
            var act = () => adapter.DropAsync(null!);
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public async Task GetInfoAsync_NullIndexName_ThrowsArgumentException()
        {
            var adapter = CreateAdapter();
            var act = () => adapter.GetInfoAsync(null!);
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
        }

        [Fact]
        public void ImplementsIIndexManager()
        {
            var adapter = CreateAdapter();
            adapter.Should().BeAssignableTo<IIndexManager>();
        }

        private static ElasticSearchIndexManagerAdapter CreateAdapter()
        {
            var settings = new Nest.ConnectionSettings(new Uri("http://localhost:9200"));
            var client = new Nest.ElasticClient(settings);
            return new ElasticSearchIndexManagerAdapter(client);
        }
    }
}
