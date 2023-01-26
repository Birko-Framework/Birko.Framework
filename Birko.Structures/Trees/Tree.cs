using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Structures.Trees
{
    public class Tree
    {
        public Node RootNode { get; set; }

        public Tree Insert(Node node)
        {
            if (node == null)
            {
                return this;
            }
            if (RootNode == null)
            {
                RootNode = node;
                RootNode.Parent = null;
            }
            else
            {
                RootNode.Insert(node);
            }
            return this;
        }

        public Tree Insert(IEnumerable<Node> nodes)
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
            if (RootNode == null)
            {
                return null;
            }
            if (RootNode.CompareTo(node) == 0)
            {
                return RootNode;
            }
            if (!(RootNode.Children?.Any() ?? false))
            {
                return null;
            }
            foreach (Node child in RootNode.Children)
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

        public Tree Remove(Node node)
        {
            if (node == null)
            {
                return this;
            }
            if (RootNode == null)
            {
                return this;
            }
            if (RootNode.CompareTo(node) == 0)
            {
                Node first = RootNode.Children?.First();
                if ((RootNode.Children?.Count() ?? 0)  > 1)
                {
                    first.Insert(RootNode.Children.Skip(1));
                    first.Parent = null;
                }
                RootNode = first;
            }
            else
            {
                foreach (Node child in RootNode.Children.ToArray()) // not use toArray
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
