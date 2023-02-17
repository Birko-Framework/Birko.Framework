using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Structures.Trees
{
    public class Tree
    {
        public Node Root { get; set; }


        public int Height
        {
            get 
            {
                return Root?.Height ?? 0;
            }
        }

        public Tree()
        {
        }

        public Tree(IEnumerable<Node> nodes) : this()
        { 
            Insert(nodes);
        }

        public virtual Node Insert(Node node)
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

        public Node Find(Node node) 
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

        public virtual Node Remove(Node node)
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
                Node first = Root.Children?.First();
                if ((Root.Children?.Count() ?? 0)  > 1)
                {
                    first.Insert(Root.Children.Skip(1));
                    first.Parent = null;
                }
                node = Root;
                Root = first;
                return node;
            }
            else
            {
                foreach (Node child in Root.Children)
                {
                    if (child.Contains(node))
                    {
                        child.Remove(node);
                        break;
                    }
                }
            }
            return node;
        }

        public IEnumerable<Node> InOrder()
        {
            return Root.InOrder();
        }

        public IEnumerable<Node> PreOrder()
        {
            return Root.PreOrder();
        }

        public IEnumerable<Node> PostOrder()
        {
            return Root.PostOrder();
        }

        public IEnumerable<Node> LevelOrder()
        {
            return Root.LevelOrder();
        }

        public override string ToString()
        {
            return string.Join("\n", PreOrder().Select(x => $"|{string.Empty.PadLeft(x.Depth, '-')} {x}"));
        }
    }
}
