using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Structures.Trees
{
    public abstract class AVLNode : BinaryNode
    {
        public int Balance 
        {
            get {
                return ((Children?.LastOrDefault() as AVLNode)?.Height ?? 0)
                       - ((Children?.FirstOrDefault() as AVLNode)?.Height ?? 0);
            }
        }

        public override Node Insert(Node node)
        {
            if (node is not AVLNode avlNode) 
            {
                throw new ArgumentException("Node is not derived from AVLNode");
            }
            return base.Insert(avlNode);
        }
    }
}
