using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Nest.JoinField;

namespace Birko.Data.Structures.Trees
{
    public class AVLTree : Tree
    {
        public AVLTree(IEnumerable<AVLNode> items) : base(items) { }

        public override Node Insert(Node node)
        {
            AVLNode insertedNode = (AVLNode)base.Insert(node);
            if (insertedNode != null || insertedNode.Parent != null)
            {
                Root = Rebalance((AVLNode)insertedNode);
            }
            return insertedNode;
        }

        public override Node Remove(Node node)
        {
            AVLNode removedNodeParent = (AVLNode)node.Parent;
            Node removedNode = base.Remove(node);
            if (removedNodeParent != null)
            {
                Root = Rebalance(removedNodeParent);
            }
            else
            {
                Root = null;
            }
            return removedNode;
        }

        protected static Node Rebalance(AVLNode node)
        {
            Node pathNode = node;
            Node newRoot = node;
            do
            {
                pathNode = Balance((AVLNode)pathNode);
                if (pathNode.Parent == null)
                {
                    newRoot = pathNode;
                }
                pathNode = pathNode.Parent;

            } while (pathNode != null);
            return newRoot;
        }

        protected static AVLNode Balance(AVLNode node)
        {
            if (node.Balance > 1) // Subtree on right has more levels
            {
                if ((node.Children.Last() as AVLNode).Balance > 0)
                {
                    return LeftRotation(node);
                }
                else
                {
                    RightRotation(node.Children.Last() as AVLNode);
                    return LeftRotation(node);
                }
            }
            else if (node.Balance < -1) //Subtree on left has more levels
            {
                if ((node.Children.First() as AVLNode).Balance < 0)
                {
                    return RightRotation(node);
                }
                else
                {
                    LeftRotation(node.Children.First() as AVLNode);
                    return RightRotation(node);
                }
            }
            return node;
        }

        protected static AVLNode RightRotation(AVLNode node)
        {
            AVLNode left = (AVLNode)node.Children.First();
            AVLNode leftRight = (AVLNode)left.Children?.Last();

            Node nodeParent = node.Parent;
            node.Parent = null;
            left.Parent = nodeParent;
            left.InsertChild(node, 1);
            node.InsertChild(leftRight, 0);

            return left;
        }

        protected static AVLNode LeftRotation(AVLNode node)
        {
            AVLNode right = (AVLNode)node.Children.Last();
            AVLNode rightLeft = (AVLNode)right.Children?.First();

            Node nodeParent = node.Parent;
            node.Parent = null;
            right.Parent = nodeParent;
            right.InsertChild(node, 0);
            node.InsertChild(rightLeft, 1);

            return right;
        }
    }
}
