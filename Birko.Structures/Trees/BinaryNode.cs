using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Data.Structures.Trees
{
    public abstract class BinaryNode: Node
    {
        internal override Node InsertChild(Node node, int index)
        {
            if (node != null)
            {
                Children ??= (new Node[2]).AsEnumerable(); // to preserve first as left and last as right
            }

            return base.InsertChild(node, index);
        }
    }
}
