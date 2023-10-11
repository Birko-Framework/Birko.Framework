using Elasticsearch.Net;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using static Nest.JoinField;

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
                index = Children.Count() - index;
            }
            if (index < 0)
            {
                throw new IndexOutOfRangeException(nameof(index));
            }

            ExtendChildren(index);

            Children = Children?.Select((x, i) =>
            {
                if (i == index)
                {
                    x?.Parent?.RemoveChild(x);
                    if (node != null)
                    {
                        node.Parent = this;
                    }
                    return node;
                }
                return x;
            })?.ToArray();

            FreeChildren();
            return node;
        }

        internal virtual int? RemoveChild(Node node,int? index = null)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            int? result = null;
            Children = Children?.Select((x, i) =>
            {
                if (
                    (index == i)
                    || (index == null && x?.CompareTo(node) == 0 && result == null)
                )
                {
                    node.Parent = null;
                    result = i;
                    return null;
                }
                return x;
            })?.ToArray();

            FreeChildren();
            return result;
        }

        private void ExtendChildren(int index)
        {
            if ((Children?.Count() ?? -1) < index)
            {
                Node[] newChildren = new Node[index + 1];
                Array.Copy(Children?.ToArray() ?? Array.Empty<Node>(), newChildren, Children?.Count() ?? 0);
                Children = newChildren;
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
