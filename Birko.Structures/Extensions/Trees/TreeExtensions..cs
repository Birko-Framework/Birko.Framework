using Birko.Data.Structures.Trees;

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
            return tree?.Root?.InOrder() ?? Array.Empty<Node>();
        }

        public static IEnumerable<Node> PreOrder(this Tree tree)
        {
            return tree?.Root?.PreOrder() ?? Array.Empty<Node>();
        }

        public static IEnumerable<Node> PostOrder(this Tree tree)
        {
            return tree?.Root?.PostOrder() ?? Array.Empty<Node>();
        }

        public static IEnumerable<Node> LevelOrder(this Tree tree)
        {
            return tree?.Root?.LevelOrder() ?? Array.Empty<Node>();
        }
    }
}
