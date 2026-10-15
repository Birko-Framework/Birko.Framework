using System;
using System.Collections.Generic;
using System.Linq;
using Elasticsearch.Net;
using Nest;

namespace Birko.Data.ElasticSearch.Stores
{
    /// <summary>
    /// The requests that repair one index's mapping drift, and the drifts they leave to an operator (TASK-634).
    /// </summary>
    /// <remarks>
    /// The ElasticSearch twin of SQL's <c>SchemaRepairPlan</c> (TASK-633). <see cref="AsyncElasticSearchStore{T}.DetectMappingDriftAsync"/>
    /// reports; this turns the report into requests a host can review and send. The framework never sends them. Only the
    /// repair that cannot lose data is written: a declared field the index lacks is added with <c>PUT _mapping</c>, built
    /// from the same descriptor the store creates the index with. A field's type cannot change in place, and a mapped field
    /// cannot be removed, so those are listed with the remedy (a new index, <c>_reindex</c>, an alias swap).
    /// </remarks>
    public sealed class MappingRepairPlan
    {
        public MappingRepairPlan(MappingDriftReport report, IReadOnlyList<MappingRepairRequest> requests, IReadOnlyList<MappingRepairSkip> notActioned)
        {
            Report = report;
            Requests = requests;
            NotActioned = notActioned;
        }

        /// <summary>The drift report the plan was made from.</summary>
        public MappingDriftReport Report { get; }

        /// <summary>The index the plan repairs.</summary>
        public string Index => Report.Index;

        /// <summary>The requests, in the order they must be sent. Empty when there is nothing the plan may repair.</summary>
        public IReadOnlyList<MappingRepairRequest> Requests { get; }

        /// <summary>Every drift the plan does not repair, each with the reason and the remedy.</summary>
        public IReadOnlyList<MappingRepairSkip> NotActioned { get; }

        /// <summary>True when the requests repair every drift.</summary>
        public bool IsComplete => NotActioned.Count == 0;
    }

    /// <summary>One HTTP request of a repair plan: what to send, to which path, with which JSON body.</summary>
    public sealed class MappingRepairRequest
    {
        public MappingRepairRequest(string method, string path, string body)
        {
            Method = method;
            Path = path;
            Body = body;
        }

        public string Method { get; }

        /// <summary>The path, relative to the cluster URL (<c>/index/_mapping</c>).</summary>
        public string Path { get; }

        /// <summary>The JSON body.</summary>
        public string Body { get; }

        public override string ToString() => $"{Method} {Path} {Body}";
    }

    /// <summary>A drift the plan leaves to an operator, and why.</summary>
    public sealed class MappingRepairSkip
    {
        public MappingRepairSkip(string drift, string reason)
        {
            Drift = drift;
            Reason = reason;
        }

        public string Drift { get; }

        public string Reason { get; }

        public override string ToString() => Drift + " (" + Reason + ")";
    }

    internal static class ElasticMappingRepair
    {
        private const string Reindex =
            "a field's type cannot change in place: create an index with the declared mapping, _reindex into it, and move "
            + "an alias to it (ReindexHelper)";

        /// <summary>
        /// The plan for <paramref name="report"/>, whose declared side is <paramref name="declared"/>.
        /// </summary>
        /// <remarks>
        /// A missing path is repaired by re-sending its whole top-level declared field: ElasticSearch merges a
        /// <c>PUT _mapping</c> into the existing mapping, adding what is new (a field, an object's sub-field, a multi-field)
        /// and refusing a change to what exists. So a top-level field with a type mismatch anywhere beneath it is not sent;
        /// the merge would be refused, and that field needs the reindex anyway.
        /// </remarks>
        public static MappingRepairPlan Plan(MappingDriftReport report, ITypeMapping? declared, Inferrer infer, IElasticsearchSerializer serializer)
        {
            var requests = new List<MappingRepairRequest>();
            var skipped = new List<MappingRepairSkip>();
            if (!report.IndexExists)
            {
                return new MappingRepairPlan(report, requests, skipped);
            }

            static string Top(string path) => path.Split('.')[0];
            var mismatchedTops = report.Drifts.Where(d => d.Kind == MappingDriftKind.TypeMismatch).Select(d => Top(d.Field)).ToHashSet(StringComparer.Ordinal);
            var addTops = new HashSet<string>(StringComparer.Ordinal);

            foreach (var drift in report.Drifts)
            {
                switch (drift.Kind)
                {
                    case MappingDriftKind.Missing when !mismatchedTops.Contains(Top(drift.Field)):
                        addTops.Add(Top(drift.Field));
                        break;
                    case MappingDriftKind.Missing:
                        skipped.Add(new MappingRepairSkip(drift.ToString(), "its field also has a type mismatch, so it is repaired by the same reindex: " + Reindex));
                        break;
                    case MappingDriftKind.TypeMismatch:
                        skipped.Add(new MappingRepairSkip(drift.ToString(), Reindex));
                        break;
                    default:
                        skipped.Add(new MappingRepairSkip(drift.ToString(),
                            "a mapped field cannot be removed in place; it goes only with a reindex into an index without it"));
                        break;
                }
            }

            if (addTops.Count > 0)
            {
                var properties = new Properties();
                foreach (var kvp in declared?.Properties ?? new Properties())
                {
                    var name = infer.PropertyName(kvp.Key) ?? kvp.Value.Name?.Name;
                    if (name != null && addTops.Contains(name))
                    {
                        properties[kvp.Key] = kvp.Value;
                    }
                }
                var body = serializer.SerializeToString(new TypeMapping { Properties = properties }, SerializationFormatting.None);
                requests.Add(new MappingRepairRequest("PUT", "/" + report.Index + "/_mapping", body));
            }

            return new MappingRepairPlan(report, requests, skipped);
        }
    }
}
