using Birko.Data.Sync.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Tests.Models;

public class SyncResultTests
{
    [Fact]
    public void SyncResult_DefaultValues_AreCorrect()
    {
        var result = new SyncResult();

        result.Success.Should().BeFalse();
        result.TotalProcessed.Should().Be(0);
        result.Created.Should().Be(0);
        result.Updated.Should().Be(0);
        result.Deleted.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Conflicts.Should().Be(0);
        result.Errors.Should().BeEmpty();
        result.IsInitialSync.Should().BeFalse();
    }

    [Fact]
    public void SyncOptions_DefaultValues_AreCorrect()
    {
        var options = new SyncOptions();

        options.Direction.Should().Be(SyncDirection.Bidirectional);
        options.ConflictPolicy.Should().Be(ConflictResolutionPolicy.NewestWins);
        options.BatchSize.Should().Be(100);
        options.MaxItems.Should().BeNull();
        options.Scope.Should().Be("Default");
        options.SkipPreview.Should().BeFalse();
    }
}
