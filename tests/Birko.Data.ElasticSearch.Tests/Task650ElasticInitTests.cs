using System;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.ElasticSearch.Tests.TestResources.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-650, refuted: the sync store's Init dereferences <c>Connector!</c> before <c>RequireConnector</c>, but it cannot
/// reach that line with no client. Without settings, the index name throws first; and SetSettings refuses any settings
/// that would leave no client (SH-M146). Pinned, so a change to either order is noticed.
/// </summary>
public class Task650ElasticInitTests
{
    [Fact]
    public void Init_without_settings_says_what_is_missing()
    {
        var store = new ElasticSearchStore<DateModel>();

        var act = () => store.Init(d => d);

        act.Should().Throw<InvalidOperationException>().WithMessage("*SetSettings*");
    }

    [Fact]
    public void Settings_that_would_leave_no_client_are_refused()
    {
        var store = new ElasticSearchStore<DateModel>();

        var act = () => store.SetSettings(new Birko.Configuration.Settings("x", "y"));

        act.Should().Throw<ArgumentException>();
    }
}
