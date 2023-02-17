using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Structures.Trees
{
    public abstract class BinaryNode: Node
    {
        public override Node Insert(Node node)
        {
            if (node == null) {
                return null;
            }
            if (node is not BinaryNode binaryNode)
            {
                throw new ArgumentException("Node is not derived from BinaryNode");
            }
            if (CompareTo(node) < 0)
            {
                if (Children?.Last() == null)
                {
                    InsertChild(binaryNode, 1);
                }
                else
                {
                    (Children.Last() as Node).Insert(node);
                }
                
            }
            else
            {
                if (Children?.First() == null)
                {
                    InsertChild(binaryNode, 0);
                }
                else
                {
                    (Children.First() as Node).Insert(node);
                }
            }

            return node;
        }

        internal override void InsertChild(Node node, int index)
        {
            if (index >= 0 && index <= 1)
            {
                Children ??= new Node[] { null, null };
            }
            base.InsertChild(node, index);
        }
    }
}
