using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Structures.Trees
{
    public abstract class Node : IComparable<Node>
    {
        public Node Parent { get; set; } = null;
        public IEnumerable<Node> Children { get; protected set; } = null;

        public abstract int CompareTo(Node other);
        public abstract Node Insert(Node node);

        public Node Insert(IEnumerable<Node> nodes)
        {
            if (nodes?.Any() ?? false)
            {
                foreach (Node node in nodes)
                {
                    Insert(node);
                }
            }
            return this;
        }

        public Node Find(Node node)
        {
            if (node == null)
            {
                return null;
            }
            if (CompareTo(node) == 0)
            {
                return this;
            }
            if (!(Children?.Any() ?? false))
            {
                return null;
            }
            foreach (Node child in Children)
            {
                Node find = child.Find(node);
                if (find != null)
                {
                    return find;
                }
            }
            return null;
        }

        public bool Contains(Node node)
        {
            return Find(node) != null;
        }

        public Node Remove(Node node)
        {
            if (node == null)
            {
                return this;
            }
            if (CompareTo(node) == 0)
            {
                IEnumerable<Node> siblings = Parent?.Children?.Where(x => x.CompareTo(node) != 0) ?? Array.Empty<Node>();
                Node first = Children?.First();
                if (first != null)
                {
                    if ((Children?.Count() ?? 0) > 1)
                    {
                        first.Insert(Children?.Skip(1));
                    }
                }
                Parent.Children = null;
                Parent?.Insert(siblings.Concat(node.Children ?? Array.Empty<Node>()).Concat(new[] { first }).Where(x => x != null));

                Children = null;
                Parent = null;
            }
            else
            {
                foreach (Node child in Children.ToArray()) // not use toArray
                {
                    if (child.Contains(node))
                    {
                        child.Remove(node);
                        break;
                    }
                }
            }
            return this;
        }
    }
}
