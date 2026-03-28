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
    }
}
