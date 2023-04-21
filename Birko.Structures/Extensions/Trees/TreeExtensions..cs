using Birko.Data.Structures.Trees;
using Nest;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Structures.Extensions.Trees
{
    public static class TreeExtensions
    {
        public static int Height(this Tree tree)
        {
            return tree?.Root?.Height() ?? 0;
        }

        public static int Count(this Tree tree)
        {
            return tree?.Root?.Count() ?? 0;
        }

        public static IEnumerable<Node> InOrder(this Tree tree)
        {
            return tree?.Root?.InOrder();
        }

        public static IEnumerable<Node> PreOrder(this Tree tree)
        {
            return tree?.Root?.PreOrder();
        }

        public static IEnumerable<Node> PostOrder(this Tree tree)
        {
            return tree?.Root?.PostOrder();
        }

        public static IEnumerable<Node> LevelOrder(this Tree tree)
        {
            return tree?.Root?.LevelOrder();
        }
    }
}
