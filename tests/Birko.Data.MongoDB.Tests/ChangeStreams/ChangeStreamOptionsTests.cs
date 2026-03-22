using FluentAssertions;
using MongoDB.Driver;
using System;
using Xunit;
using ChangeStreamOptions = Birko.Data.MongoDB.ChangeStreams.ChangeStreamOptions;

namespace Birko.Data.MongoDB.Tests.ChangeStreams;

public class ChangeStreamOptionsTests
{
    [Fact]
    public void FullDocument_DefaultsToUpdateLookup()
    {
        var options = new ChangeStreamOptions();

        options.FullDocument.Should().Be(ChangeStreamFullDocumentOption.UpdateLookup);
    }

    [Fact]
    public void BatchSize_DefaultsToNull()
    {
        var options = new ChangeStreamOptions();

        options.BatchSize.Should().BeNull();
    }

    [Fact]
    public void MaxAwaitTime_DefaultsToNull()
    {
        var options = new ChangeStreamOptions();

        options.MaxAwaitTime.Should().BeNull();
    }

    [Fact]
    public void ResumeAfter_DefaultsToNull()
    {
        var options = new ChangeStreamOptions();

        options.ResumeAfter.Should().BeNull();
    }

    [Fact]
    public void StartAfter_DefaultsToNull()
    {
        var options = new ChangeStreamOptions();

        options.StartAfter.Should().BeNull();
    }

    [Fact]
    public void BatchSize_CanBeSet()
    {
        var options = new ChangeStreamOptions { BatchSize = 100 };

        options.BatchSize.Should().Be(100);
    }

    [Fact]
    public void MaxAwaitTime_CanBeSet()
    {
        var options = new ChangeStreamOptions { MaxAwaitTime = TimeSpan.FromSeconds(30) };

        options.MaxAwaitTime.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void FullDocument_CanBeChanged()
    {
        var options = new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.Default };

        options.FullDocument.Should().Be(ChangeStreamFullDocumentOption.Default);
    }
}
