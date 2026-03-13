using Birko.Structures.Extensions.Trees;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Structures.Trees
{
    public class Tree
    {
        public Node? Root { get; protected set; }

        public Tree()
        {
        }

        public Tree(IEnumerable<Node> nodes) : this()
        {
            Insert(nodes);
        }

        public virtual Node? Insert(Node? node)
        {
            if (node == null)
            {
                return null;
            }
            if (Root == null)
            {
                Root = node;
                Root.Parent = null;
                return Root;
            }
            else
            {
                return Root.Insert(node);
            }
        }

        public void Insert(IEnumerable<Node> nodes)
        {
            if (nodes?.Any() ?? false)
            {
                foreach (Node node in nodes)
                {
                    Insert(node);
                }
            }
        }

        public Node? Find(Node? node)
        {
            if (node == null)
            {
                return null;
            }
            if (Root == null)
            {
                return null;
            }
            return Root.Find(node);
        }

        public bool Contains(Node node)
        {
            return Find(node) != null;
        }

        public virtual Node? Remove(Node? node)
        {
            if (node == null)
            {
                return null;
            }
            if (Root == null)
            {
                return node;
            }
            if (Root.CompareTo(node) == 0)
            {
                Node? first = Root.Children?.First();
                if (first != null && (Root.Children?.Count() ?? 0) > 1)
                {
                    var remaining = Root.Children!.Skip(1).Where(x => x != null).Cast<Node>();
                    first.Insert(remaining);
                    first.Parent = null;
                }
                node = Root;
                Root = first;
                return node;
            }
            else
            {
                if (Root.Children != null)
                {
                    foreach (Node? child in Root.Children)
                    {
                        if (child != null && child.Contains(node))
                        {
                            child.Remove(node);
                            break;
                        }
                    }
                }
            }
            return node;
        }
    }
}
