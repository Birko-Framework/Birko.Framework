using Birko.Data.ElasticSearch.IndexManagement;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests.IndexManagement
{
    public class ReindexHelperTests
    {
        [Fact]
        public void Constructor_NullClient_ThrowsArgumentNullException()
        {
            var act = () => new ReindexHelper(null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("client");
        }

        [Fact]
        public void Constructor_WithIndexManager_NullClient_ThrowsArgumentNullException()
        {
            var manager = CreateManager();
            var act = () => new ReindexHelper(null!, manager);
            act.Should().Throw<ArgumentNullException>().WithParameterName("client");
        }

        [Fact]
        public void Constructor_WithIndexManager_NullManager_ThrowsArgumentNullException()
        {
            var client = CreateClient();
            var act = () => new ReindexHelper(client, null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("indexManager");
        }

        [Fact]
        public void Reindex_NullSource_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.Reindex(null!, "target");
            act.Should().Throw<ArgumentException>().WithParameterName("sourceIndex");
        }

        [Fact]
        public void Reindex_NullTarget_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.Reindex("source", null!);
            act.Should().Throw<ArgumentException>().WithParameterName("targetIndex");
        }

        [Fact]
        public void Reindex_SameSourceAndTarget_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.Reindex("same-index", "same-index");
            act.Should().Throw<ArgumentException>().WithMessage("*different*");
        }

        [Fact]
        public void Reindex_SameSourceAndTarget_CaseInsensitive_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.Reindex("Same-Index", "same-index");
            act.Should().Throw<ArgumentException>().WithMessage("*different*");
        }

        [Fact]
        public void ReindexWithScript_NullScript_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.ReindexWithScript("source", "target", null!);
            act.Should().Throw<ArgumentException>().WithParameterName("scriptSource");
        }

        [Fact]
        public void ReindexWithScript_EmptyScript_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.ReindexWithScript("source", "target", "");
            act.Should().Throw<ArgumentException>().WithParameterName("scriptSource");
        }

        [Fact]
        public void ReindexWithAlias_NullAlias_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.ReindexWithAlias(null!, "new-index");
            act.Should().Throw<ArgumentException>().WithParameterName("aliasName");
        }

        [Fact]
        public void ReindexWithAlias_NullNewIndex_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.ReindexWithAlias("alias", null!);
            act.Should().Throw<ArgumentException>().WithParameterName("newIndexName");
        }

        [Fact]
        public async System.Threading.Tasks.Task ReindexAsync_NullSource_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.ReindexAsync(null!, "target");
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("sourceIndex");
        }

        [Fact]
        public async System.Threading.Tasks.Task ReindexAsync_NullTarget_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.ReindexAsync("source", null!);
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("targetIndex");
        }

        [Fact]
        public async System.Threading.Tasks.Task ReindexAsync_SameSourceAndTarget_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.ReindexAsync("same", "same");
            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*different*");
        }

        [Fact]
        public async System.Threading.Tasks.Task ReindexWithScriptAsync_NullScript_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.ReindexWithScriptAsync("source", "target", null!);
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("scriptSource");
        }

        [Fact]
        public async System.Threading.Tasks.Task ReindexWithAliasAsync_NullAlias_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.ReindexWithAliasAsync(null!, "new-index");
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("aliasName");
        }

        [Fact]
        public async System.Threading.Tasks.Task ReindexWithAliasAsync_NullNewIndex_ThrowsArgumentException()
        {
            var helper = CreateHelper();
            var act = () => helper.ReindexWithAliasAsync("alias", null!);
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("newIndexName");
        }

        private static Nest.ElasticClient CreateClient()
        {
            var settings = new Nest.ConnectionSettings(new Uri("http://localhost:19200"))
                .DisableDirectStreaming();
            return new Nest.ElasticClient(settings);
        }

        private static IndexManager CreateManager()
        {
            return new IndexManager(CreateClient());
        }

        private static ReindexHelper CreateHelper()
        {
            return new ReindexHelper(CreateClient());
        }
    }
}
