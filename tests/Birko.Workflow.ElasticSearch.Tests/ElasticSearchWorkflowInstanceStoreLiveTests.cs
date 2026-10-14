using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Birko.Workflow.Core;
using Birko.Workflow.Execution;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Workflow.ElasticSearch.Tests;

/// <summary>
/// TASK-613 — the ElasticSearch workflow instance store against a live server. The model maps
/// <c>workflowName</c> / <c>currentState</c> as <c>keyword</c> fields themselves; a filter that queried
/// <c>workflowName.keyword</c> named a field that does not exist and matched nothing, without an error.
/// <para>Gated on <c>BIRKO_ES_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public sealed class ElasticSearchWorkflowInstanceStoreLiveTests : IDisposable
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");

    private readonly ITestOutputHelper _output;
    private readonly string _prefix = "birko613" + Guid.NewGuid().ToString("N");

    public ElasticSearchWorkflowInstanceStoreLiveTests(ITestOutputHelper output) => _output = output;

    private bool Ready()
    {
        if (!string.IsNullOrWhiteSpace(Url)) return true;
        const string message = "SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL (e.g. http://localhost:9200); "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private static void Refresh() => Http.PostAsync(Url!.TrimEnd('/') + "/_refresh", null).GetAwaiter().GetResult().EnsureSuccessStatusCode();

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { Http.DeleteAsync(Url.TrimEnd('/') + "/" + _prefix + "*").GetAwaiter().GetResult(); } catch { }
    }

    private class OrderData
    {
        public string OrderId { get; set; } = string.Empty;
    }

    private ElasticSearchWorkflowInstanceStore<OrderData> NewStore()
        => new(new Birko.Data.ElasticSearch.Stores.Settings { Location = Url!, Name = _prefix });

    private static WorkflowInstance<OrderData> Instance(string state, WorkflowStatus status) =>
        WorkflowInstance<OrderData>.Restore(Guid.NewGuid(), state, status, new OrderData { OrderId = "ORD-1" },
            new List<StateChangeRecord>());

    [Fact]
    public async Task Saved_instances_are_found_by_workflow_name_state_and_status()
    {
        if (!Ready()) return;
        var store = NewStore();

        var submitted = Instance("Submitted", WorkflowStatus.Active);
        var approved = Instance("Approved", WorkflowStatus.Completed);
        await store.SaveAsync("OrderWorkflow", submitted);
        await store.SaveAsync("OrderWorkflow", approved);
        await store.SaveAsync("OtherWorkflow", Instance("Submitted", WorkflowStatus.Active));
        Refresh();

        (await store.FindByWorkflowNameAsync("OrderWorkflow")).Select(i => i.InstanceGuid)
            .Should().BeEquivalentTo(new[] { submitted.InstanceGuid, approved.InstanceGuid });
        (await store.FindByStateAsync("OrderWorkflow", "Submitted")).Select(i => i.InstanceGuid)
            .Should().Equal(submitted.InstanceGuid);
        (await store.FindByStatusAsync("OrderWorkflow", WorkflowStatus.Completed)).Select(i => i.InstanceGuid)
            .Should().Equal(approved.InstanceGuid);
    }
}
