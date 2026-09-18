using Birko.Data.ElasticSearch.IndexManagement;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests.IndexManagement
{
    public class IndexInfoTests
    {
        [Fact]
        public void IndexInfo_DefaultValues_AreCorrect()
        {
            var info = new IndexInfo();

            info.Name.Should().BeNull();
            info.DocumentCount.Should().Be(0);
            info.SizeInBytes.Should().Be(0);
            info.NumberOfShards.Should().Be(0);
            info.NumberOfReplicas.Should().Be(0);
            info.Aliases.Should().BeEmpty();
            info.Health.Should().Be("unknown");
            info.State.Should().Be("open");
            info.RefreshInterval.Should().BeNull();
        }

        [Fact]
        public void IndexInfo_SetProperties_ReturnsSetValues()
        {
            var info = new IndexInfo
            {
                Name = "test-index",
                DocumentCount = 1000,
                SizeInBytes = 50000,
                NumberOfShards = 3,
                NumberOfReplicas = 1,
                Aliases = new[] { "alias1", "alias2" },
                Health = "green",
                State = "open",
                RefreshInterval = "1s"
            };

            info.Name.Should().Be("test-index");
            info.DocumentCount.Should().Be(1000);
            info.SizeInBytes.Should().Be(50000);
            info.NumberOfShards.Should().Be(3);
            info.NumberOfReplicas.Should().Be(1);
            info.Aliases.Should().HaveCount(2);
            info.Health.Should().Be("green");
            info.RefreshInterval.Should().Be("1s");
        }
    }

    public class ReindexResultTests
    {
        [Fact]
        public void Successful_CreatesCorrectResult()
        {
            var duration = TimeSpan.FromSeconds(5);
            var result = ReindexResult.Successful("source", "target", 1000, duration);

            result.Success.Should().BeTrue();
            result.SourceIndex.Should().Be("source");
            result.TargetIndex.Should().Be("target");
            result.DocumentsProcessed.Should().Be(1000);
            result.Duration.Should().Be(duration);
            result.ErrorMessage.Should().BeNull();
            result.Failures.Should().Be(0);
        }

        [Fact]
        public void Failed_CreatesCorrectResult()
        {
            var result = ReindexResult.Failed("source", "target", "Something went wrong", 500, 10);

            result.Success.Should().BeFalse();
            result.SourceIndex.Should().Be("source");
            result.TargetIndex.Should().Be("target");
            result.ErrorMessage.Should().Be("Something went wrong");
            result.DocumentsProcessed.Should().Be(500);
            result.Failures.Should().Be(10);
        }

        [Fact]
        public void Failed_WithDefaults_HasZeroCounts()
        {
            var result = ReindexResult.Failed("source", "target", "error");

            result.DocumentsProcessed.Should().Be(0);
            result.Failures.Should().Be(0);
        }
    }
}
