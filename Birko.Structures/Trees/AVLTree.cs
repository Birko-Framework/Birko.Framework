using Birko.Structures.Extensions.Trees;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Structures.Trees
{
    public class AVLTree : Tree
    {
        public AVLTree(IEnumerable<BinarySearchNode> items) : base(items) { }

        public override Node? Insert(Node? node)
        {
            if (node is not BinarySearchNode)
            {
                throw new ArgumentException("Argument is not a instance of BinarySearchNode");
            }
            BinarySearchNode? insertedNode = (BinarySearchNode?)base.Insert(node);
            if (insertedNode != null)
            {
                Root = ReBalance(insertedNode);
            }
            return insertedNode;
        }

        public override Node? Remove(Node? node)
        {
            if (node is not BinarySearchNode)
            {
                throw new ArgumentException("Argument is not a instance of BinarySearchNode");
            }
            BinarySearchNode? removedNodeParent = node.Parent as BinarySearchNode;
            Node? removedNode = base.Remove(node);
            if (removedNodeParent != null)
            {
                Root = ReBalance(removedNodeParent);
            }
            else
            {
                Root = null;
            }
            return removedNode;
        }

        protected static Node ReBalance(BinarySearchNode node)
        {
            Node? pathNode = (Node?)node;
            Node newRoot = pathNode!;
            do
            {
                pathNode = ((BinarySearchNode)pathNode!).ReBalance();
                if (pathNode.Parent == null)
                {
                    newRoot = pathNode;
                }
                pathNode = pathNode.Parent;

            } while (pathNode != null);
            return newRoot;
        }
    }
}
