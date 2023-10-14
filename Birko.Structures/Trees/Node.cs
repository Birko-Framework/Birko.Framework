using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Structures.Trees
{
    public abstract class Node : IComparable<Node>
    {
        public Node Parent { get; set; }
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
            
            return FindInChildren(node);
        }

        protected virtual Node FindInChildren(Node node)
        {
            if (node == null)
            {
                return null;
            }
            if (!(Children?.Any(x => x != null) ?? false))
            {
                return null;
            }
            foreach (Node child in Children.Where(x => x != null))
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

        public virtual Node Remove(Node node)
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
            else if(Children?.Any() ?? false)
            {
                foreach (Node child in Children)
                {
                    if (child?.Contains(node) ?? false)
                    {
                        child.Remove(node);
                        break;
                    }
                }
            }
            return this;
        }

        internal virtual Node InsertChild(Node node, int index)
        {
            if (index < 0)
            {
                index = (Children?.Count() ?? 0) - index;
            }
            if (index < 0)
            {
                throw new IndexOutOfRangeException(nameof(index));
            }

            ExtendChildren(index);

            if (Children?.Any() ?? false)
            {
                List<Node> newChildren = new();
                var i = 0;
                foreach (Node child in Children)
                {
                    if (i == index)
                    {
                        if(child != null)
                        {
                            child.Parent = null;
                        }
                        if (node != null)
                        {
                            node.Parent = this;
                        }
                    }
                    newChildren.Add((i == index) ? node : child);
                    i++;
                }
                Children = newChildren.AsEnumerable();
                FreeChildren();
            }

            return node;
        }

        internal virtual int? RemoveChild(Node node, int? index = null)
        {
            if (index != null && index < 0)
            {
                index = (Children?.Count() ?? 0) - index;
            }

            if (index != null && index < 0)
            {
                throw new IndexOutOfRangeException(nameof(index));
            }

            int? result = null;
            if (Children?.Any() ?? false)
            {
                List<Node> newChildren = new();
                var i = 0;
                foreach (Node child in Children)
                {
                    if (
                        (index != null && i == index)
                        || (index == null && result == null && child != null && child.CompareTo(node) == 0)
                    )
                    {
                        if(child != null)
                        {
                            child.Parent = null;
                        }
                        result = i;
                    }
                    newChildren.Add((result != i) ? child : null);
                    i++;
                }
                Children = newChildren.AsEnumerable();
                FreeChildren();
            }

            return result;
        }

        private void ExtendChildren(int index)
        {
            if ((Children?.Count() ?? -1) < index)
            {
                Node[] newChildren = new Node[index + 1];
                Array.Copy(Children?.ToArray() ?? Array.Empty<Node>(), newChildren, Children?.Count() ?? 0);
                Children = newChildren.AsEnumerable();
            }
        }

        private void FreeChildren()
        {
            if ((Children?.All(x => x == null) ?? false) || (Children?.Count() ?? 0) == 0)
            {
                Children = null;
            }
        }
    }
}
