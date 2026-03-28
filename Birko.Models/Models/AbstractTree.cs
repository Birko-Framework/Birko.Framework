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
                Path = BuildPath(data.Path) ?? string.Empty;
            }
        }

        public static string? BuildPath(IEnumerable<Guid> path)
        {
            return PathSeparator + ((path?.Any() ?? false)
                ? string.Join(PathSeparator, path.Select(x => x.ToString("B")).Distinct())
                : null);
        }
    }
}
