using Birko.Data.Structures.Trees;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Structures.Extensions.Trees
{
    public static class NodeExtensions
    {
        public static int Depth(this Node node)
        {
            return (node?.Parent?.Depth() ?? -1) + 1;
        }

        public static int Height(this Node node)
        {
            return (node?.Children?.Max(x => x?.Height() ?? 0) ?? 0) + 1;
        }
        
        public static int Count(this Node node)
        {
            return (node?.Children?.Sum(x => x?.Count() ?? 0) ?? 0) + 1;
        }

        public static IEnumerable<Node> InOrder(this Node node)
        {
            bool wasThis = false;
            if (node?.Children?.Any(x => x != null) ?? false)
            {
                foreach (Node child in node.Children.Where(x => x != null))
                {
                    if (node.CompareTo(child) < 0 && !wasThis)
                    {
                        wasThis = true;
                        yield return node;
                    }
                    foreach (Node childNode in child.InOrder())
                    {
                        yield return childNode;
                    }
                }
            }
            if (!wasThis)
            {
                yield return node;
            }
        }

        public static IEnumerable<Node> PreOrder(this Node node)
        {
            yield return node;
            if (node?.Children?.Any(x => x != null) ?? false)
            {
                foreach (Node child in node.Children.Where(x => x != null))
                {
                    foreach (var childNode in child.PreOrder())
                    {
                        yield return childNode;
                    }
                }
            }
        }

        public static IEnumerable<Node> PostOrder(this Node node)
        {
            if (node?.Children?.Any(x => x != null) ?? false)
            {
                foreach (Node child in node?.Children.Reverse().Where(x => x != null))
                {
                    foreach (var childNode in child.PostOrder())
                    {
                        yield return childNode;
                    }
                }
            }
            yield return node;
        }

        public static IEnumerable<Node> LevelOrder(this Node node)
        {
            return ProcessLevelNode(new List<Node>() { node });
        }

        private static IEnumerable<Node> ProcessLevelNode(IList<Node> list)
        {
            while (list.Any())
            {
                yield return list.First();

                if ((list.First().Children?.Count(x => x != null) ?? 0) > 0)
                {
                    foreach (Node child in list.First().Children?.Where(x => x != null))
                    {
                        list.Add(child);
                    }
                }
                list.RemoveAt(0);
            }
        }
    }
}
