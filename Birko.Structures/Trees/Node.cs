using Elasticsearch.Net;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Nest.JoinField;

namespace Birko.Data.Structures.Trees
{
    public abstract class Node : IComparable<Node>
    {
        private Node _parent = null;
        public Node Parent
        {
            get {
                return _parent;
            }
            set
            {
                bool isSameParent = (Parent == null && value == null) || (Parent != null && value != null && Parent.CompareTo(value) == 0);
                if (!isSameParent)
                {
                    //Remove node as child from parent childs
                    if (Parent != null && (value == null || Parent.CompareTo(value) != 0))
                    {
                        Parent.Children = Parent.Children?.Select(x => ((x?.CompareTo(this) ?? 0) == 0) ? null : x);
                    }
                    _parent = value;
                    // Try insert node as parent child
                    if (Parent != null && !(Parent.Children?.Any(x => x != null && x.CompareTo(this) == 0) ?? false))
                    {
                        Parent.Insert(this);
                    }
                }
            }
        }
        public IEnumerable<Node> Children { get; protected set; } = null;

        public int Depth
        {
            get 
            {
                return (Parent?.Depth ?? -1) + 1;
            }
        }

        public int Height
        {
            get
            {
                return (Children?.Max(x => x?.Height ?? 0) ?? 0) + 1;
            }
        }

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

        public IEnumerable<Node> InOrder()
        {
            bool wasThis = false;
            if (Children?.Any(x=> x!= null) ?? false)
            {
                foreach (Node child in Children.Where(x => x != null))
                {
                    if (CompareTo(child) < 0 && !wasThis)
                    {
                        wasThis = true;
                        yield return this;
                    }
                    foreach (Node node in child.InOrder())
                    {
                        yield return node;
                    }
                }
            }
            if(!wasThis) 
            {
                yield return this;
            }
        }

        public IEnumerable<Node> PreOrder()
        {
            yield return this;
            if (Children?.Any(x => x != null) ?? false)
            {
                foreach (Node child in Children.Where(x => x != null))
                {
                    foreach (var node in child.PreOrder())
                    {
                        yield return node;
                    }
                }
            }
        }

        public IEnumerable<Node> PostOrder()
        {
            if (Children?.Any(x => x != null) ?? false)
            {
                foreach (Node child in Children.Reverse().Where(x => x != null))
                {
                    foreach (var node in child.PostOrder())
                    {
                        yield return node;
                    }
                }
            }
            yield return this;
        }

        public IEnumerable<Node> LevelOrder()
        {
            return ProcessLevelNode(new List<Node>(){ this });
        }

        private static IEnumerable<Node> ProcessLevelNode(IList<Node> list) {
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

        internal virtual void InsertChild(Node node, int index)
        {
            if (index >= (Children?.Count() ?? 0))
            {
                Children = null;
                return;
            }

            Children = Children.Select((x, i) => (i == index) ? node : x);
            if (Children.All(x => x == null) || Children.Count() == 0)
            {
                Children = null;
            }
            if (node != null)
            {
                node.Parent = this;
            }
        }
    }
}
