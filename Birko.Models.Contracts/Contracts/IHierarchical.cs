using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Models.Contracts
{
    /// <summary>
    /// Entity that participates in a parent-child hierarchy.
    /// Path is a materialized path of ancestor GUIDs: "/rootGuid/parentGuid/selfGuid".
    /// Enables efficient subtree queries via prefix matching (SQL LIKE 'path/%').
    /// </summary>
    public interface IHierarchical
    {
        Guid? ParentGuid { get; set; }

        /// <summary>
        /// Materialized path of ancestor IDs: "/rootGuid/parentGuid/selfGuid".
        /// Root nodes have path "/{selfGuid}".
        /// </summary>
        string Path { get; set; }

        /// <summary>
        /// Depth in the hierarchy. 0 = root, 1 = first-level child, etc.
        /// </summary>
        int Depth { get; set; }
    }

    /// <summary>
    /// Optional extension for hierarchical entities that also store a human-readable name path
    /// (e.g., "Electronics/Phones/Smartphones"). Useful for breadcrumbs and display.
    /// </summary>
    public interface INamedHierarchical : IHierarchical
    {
        /// <summary>
        /// Human-readable name for this node (used to build NamePath).
        /// </summary>
        string HierarchyName { get; }

        /// <summary>
        /// Materialized name path: "Electronics/Phones/Smartphones".
        /// Updated when any ancestor is renamed.
        /// </summary>
        string NamePath { get; set; }
    }

    /// <summary>
    /// Utilities for computing and cascading materialized paths on IHierarchical entities.
    /// </summary>
    public static class HierarchyHelper
    {
        public const string Separator = "/";

        /// <summary>
        /// Computes Path and Depth for a node given its parent.
        /// The node's Guid must be set before calling.
        /// If the node implements INamedHierarchical, NamePath is also computed.
        /// </summary>
        public static void ComputePath<T>(T node, T? parent) where T : Data.Models.AbstractModel, IHierarchical
        {
            var id = node.Guid ?? throw new InvalidOperationException("Node Guid must be set before computing path.");
            if (parent is not null)
            {
                node.Path = $"{parent.Path}{Separator}{id}";
                node.Depth = parent.Depth + 1;
            }
            else
            {
                node.Path = $"{Separator}{id}";
                node.Depth = 0;
            }

            // Also compute NamePath if the entity supports it
            if (node is INamedHierarchical named)
            {
                var parentNamed = parent as INamedHierarchical;
                named.NamePath = parentNamed is not null
                    ? $"{parentNamed.NamePath}{Separator}{named.HierarchyName}"
                    : named.HierarchyName;
            }
        }

        /// <summary>
        /// Derives <see cref="IHierarchical.ParentGuid"/> and <see cref="IHierarchical.Depth"/> from the
        /// node's already-materialized <see cref="IHierarchical.Path"/> ("/root/parent/self"). Use this to
        /// restore hierarchy position after a Path-only round-trip (e.g. a ViewModel that carries Path but
        /// not ParentGuid/Depth), keeping Path the single source of truth. Depends only on
        /// <see cref="IHierarchical"/> (no persistence base type), so it stays inside the zero-dep contract.
        /// </summary>
        public static void DeriveParentAndDepthFromPath(IHierarchical node)
        {
            if (node is null)
            {
                return;
            }

            var segments = (node.Path ?? string.Empty)
                .Split(Separator, StringSplitOptions.RemoveEmptyEntries);

            // "/self" → depth 0; each additional ancestor segment adds one level.
            node.Depth = segments.Length > 0 ? segments.Length - 1 : 0;

            // The parent is the segment immediately before self (second-to-last), if any.
            // Guid.TryParse accepts the D/B/N/P formats used across the codebase.
            node.ParentGuid = segments.Length >= 2 && Guid.TryParse(segments[segments.Length - 2], out var parentId)
                ? parentId
                : (Guid?)null;
        }

        /// <summary>
        /// Rewrites Path (and optionally NamePath) for all descendants after an ancestor's path changed.
        /// Call after updating the ancestor entity.
        /// </summary>
        /// <param name="descendants">All entities whose Path starts with oldPath + "/".</param>
        /// <param name="oldPath">The ancestor's path before the change.</param>
        /// <param name="newPath">The ancestor's path after the change.</param>
        /// <param name="oldNamePath">Optional: the ancestor's name path before (null to skip name path rewrite).</param>
        /// <param name="newNamePath">Optional: the ancestor's name path after.</param>
        public static void RewriteDescendantPaths<T>(
            IEnumerable<T> descendants,
            string oldPath, string newPath,
            string? oldNamePath = null, string? newNamePath = null)
            where T : IHierarchical
        {
            foreach (var desc in descendants)
            {
                desc.Path = newPath + desc.Path.Substring(oldPath.Length);
                desc.Depth = desc.Path.Count(c => c == '/') - 1;

                if (oldNamePath is not null && newNamePath is not null && desc is INamedHierarchical named)
                {
                    var suffix = named.NamePath.Length > oldNamePath.Length
                        ? named.NamePath.Substring(oldNamePath.Length).TrimStart('/')
                        : string.Empty;
                    named.NamePath = string.IsNullOrEmpty(suffix)
                        ? newNamePath
                        : $"{newNamePath}{Separator}{suffix}";
                }
            }
        }

        /// <summary>
        /// Returns true if <paramref name="potentialDescendant"/>'s path indicates
        /// it is a descendant of <paramref name="potentialAncestor"/>.
        /// </summary>
        public static bool IsDescendantOf(IHierarchical potentialDescendant, IHierarchical potentialAncestor)
        {
            return potentialDescendant.Path.StartsWith(potentialAncestor.Path + Separator);
        }

        /// <summary>
        /// Resequences SortOrder on an ordered list of items to 0, 1, 2...
        /// Items must already be sorted by their current SortOrder.
        /// Returns only the items whose SortOrder actually changed (for efficient bulk update).
        /// </summary>
        public static IReadOnlyList<T> NormalizeSortOrder<T>(IReadOnlyList<T> sortedItems, Func<T, int> getOrder, Action<T, int> setOrder)
        {
            var changed = new List<T>();
            for (int i = 0; i < sortedItems.Count; i++)
            {
                if (getOrder(sortedItems[i]) != i)
                {
                    setOrder(sortedItems[i], i);
                    changed.Add(sortedItems[i]);
                }
            }
            return changed;
        }
    }

    /// <summary>
    /// Entity with a SortOrder property for manual ordering within a collection.
    /// </summary>
    public interface ISortable
    {
        int SortOrder { get; set; }
    }
}
