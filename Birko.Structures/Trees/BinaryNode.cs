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
            if (node is not BinaryNode binaryNode)
            {
                return this;
            }
            Children ??= new BinaryNode[] { null, null };
            if (CompareTo(node) <= 0)
            {
                if (Children.First() == null)
                {
                    (Children as BinaryNode[])[0] = binaryNode;
                }
                else
                {
                    Children.First().Insert(node);
                }
            }
            else
            {
                if (Children.Last() == null)
                {
                    (Children as BinaryNode[])[1] = binaryNode;
                }
                else
                {
                    Children.Last().Insert(node);
                }
            }
            return this;
        }
    }
}
