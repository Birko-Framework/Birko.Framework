using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Models;

namespace Birko.Models
{
    public interface ITreePath
    {
        string Path { get; set; }
    }

    public abstract class AbstractTree
        : AbstractLogModel
        , ITreePath
        , Birko.Data.Models.ILoadable<ViewModels.AbstractTree>
        , Birko.Models.Contracts.IHierarchical
    {
        public const string PathSeparator = "/";

        public virtual Guid? ParentGuid { get; set; }

        public virtual string Path { get; set; } = null!;

        public virtual int Depth { get; set; }

        public virtual void LoadFrom(ViewModels.AbstractTree data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                Path = BuildPath(data.Path);
            }
        }

        /// <summary>
        /// Builds the slash-separated materialized path from an ancestor Guid sequence. A null/empty
        /// sequence yields the root sentinel <see cref="PathSeparator"/> ("/") — every path is rooted at
        /// the separator, so this never returns null (CR-L302). Guids are formatted "B" and de-duplicated.
        /// </summary>
        public static string BuildPath(IEnumerable<Guid> path)
        {
            // Enumerate once (the old Any()+Select double-enumerated the sequence).
            var segments = path?.Select(x => x.ToString("B")).Distinct().ToList();
            return segments is { Count: > 0 }
                ? PathSeparator + string.Join(PathSeparator, segments)
                : PathSeparator;
        }
    }
}
