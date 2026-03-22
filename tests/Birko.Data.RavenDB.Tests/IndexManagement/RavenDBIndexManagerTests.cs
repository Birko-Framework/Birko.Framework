using Birko.Data.RavenDB.IndexManagement;
using FluentAssertions;
using Moq;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using System;
using Xunit;
using IndexDefinition = Birko.Data.Patterns.IndexManagement.IndexDefinition;

namespace Birko.Data.RavenDB.Tests.IndexManagement;

public class RavenDBIndexManagerTests
{
    private static RavenDBIndexManager CreateManager()
    {
        // IDocumentStore mock — validation tests throw before any Maintenance/Session calls
        var mockStore = new Mock<IDocumentStore>();
        mockStore.Setup(s => s.Database).Returns("TestDb");
        return new RavenDBIndexManager(mockStore.Object);
    }

    [Fact]
    public void Constructor_NullDocumentStore_ThrowsArgumentNullException()
    {
        var act = () => new RavenDBIndexManager(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("documentStore");
    }

    #region ExistsAsync

    [Fact]
    public async Task ExistsAsync_NullIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.ExistsAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    [Fact]
    public async Task ExistsAsync_EmptyIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.ExistsAsync("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_NullDefinition_ThrowsArgumentNullException()
    {
        var manager = CreateManager();

        var act = () => manager.CreateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("definition");
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ThrowsArgumentException()
    {
        var manager = CreateManager();
        var definition = new IndexDefinition { Name = "" };

        var act = () => manager.CreateAsync(definition);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("definition");
    }

    #endregion

    #region DropAsync

    [Fact]
    public async Task DropAsync_NullIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.DropAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    [Fact]
    public async Task DropAsync_EmptyIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.DropAsync("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    #endregion

    #region GetInfoAsync

    [Fact]
    public async Task GetInfoAsync_NullIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.GetInfoAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    #endregion

    #region ResetAsync

    [Fact]
    public async Task ResetAsync_EmptyIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.ResetAsync("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    #endregion

    #region EnableAsync / DisableAsync

    [Fact]
    public async Task EnableAsync_EmptyIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.EnableAsync("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    [Fact]
    public async Task DisableAsync_EmptyIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.DisableAsync("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    #endregion

    #region DeployFromAssemblyAsync

    [Fact]
    public async Task DeployFromAssemblyAsync_NullAssembly_ThrowsArgumentNullException()
    {
        var manager = CreateManager();

        var act = () => manager.DeployFromAssemblyAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("assembly");
    }

    #endregion

    #region QueryMapReduceAsync

    [Fact]
    public async Task QueryMapReduceAsync_NullIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.QueryMapReduceAsync<object>(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    [Fact]
    public async Task QueryMapReduceAsync_EmptyIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.QueryMapReduceAsync<object>("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    #endregion

    #region QueryMapReduceFirstAsync

    [Fact]
    public async Task QueryMapReduceFirstAsync_NullIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.QueryMapReduceFirstAsync<object>(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    [Fact]
    public async Task QueryMapReduceFirstAsync_EmptyIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.QueryMapReduceFirstAsync<object>("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    #endregion

    #region CountMapReduceAsync

    [Fact]
    public async Task CountMapReduceAsync_NullIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.CountMapReduceAsync<object>(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    [Fact]
    public async Task CountMapReduceAsync_EmptyIndexName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var act = () => manager.CountMapReduceAsync<object>("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("indexName");
    }

    #endregion
}
