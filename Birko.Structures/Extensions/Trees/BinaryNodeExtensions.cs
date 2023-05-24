using Birko.Data.Structures.Trees;
using Nest;
using System.Linq;

namespace Birko.Data.Structures.Extensions.Trees
{
    public static class BinaryNodeExtensions
    {
        public static int Balance(this BinaryNode node)
        {
            return (node?.Children?.LastOrDefault()?.Height() ?? 0)
                - (node?.Children?.FirstOrDefault()?.Height() ?? 0);
        }

        public static BinaryNode RightRotation(this BinaryNode node)
        {
            BinaryNode left = (BinaryNode)node.Children.First();
            BinaryNode leftRight = (BinaryNode)left.Children?.Last();

            Node nodeParent = node.Parent;
            int nodeIndex = nodeParent?.RemoveChild(node) ?? -1;
            node.RemoveChild(left, 0);
            if (leftRight != null)
            {
                left.RemoveChild(leftRight, 1);
            }

            node.Parent = null;
            if (nodeIndex >= 0)
            {
                nodeParent?.InsertChild(left, nodeIndex);
            }
            left.InsertChild(node, 1);
            node.InsertChild(leftRight, 0);

            return left;
        }

        public static BinaryNode LeftRotation(this BinaryNode node)
        {
            BinaryNode right = (BinaryNode)node.Children.Last();
            BinaryNode rightLeft = (BinaryNode)right.Children?.First();

            Node nodeParent = node.Parent;
            int nodeIndex = nodeParent?.RemoveChild(node) ?? -1;
            node.RemoveChild(right, 1);
            if (rightLeft != null)
            {
                right.RemoveChild(rightLeft, 0);
            }

            node.Parent = null;
            if (nodeIndex >= 0)
            {
                nodeParent?.InsertChild(right, nodeIndex);
            }
            right.InsertChild(node, 0);
            node.InsertChild(rightLeft, 1);

            return right;
        }

        public static BinaryNode ReBalance(this BinaryNode node)
        {
            if (node.Balance() > 1) // Subtree on right has more levels
            {
                if ((node.Children.Last() as BinaryNode).Balance() < 0)
                {
                    (node.Children.Last() as BinaryNode).RightRotation();
                }
                return node.LeftRotation();
            }
            else if (node.Balance() < -1) //Subtree on left has more levels
            {
                if ((node.Children.First() as BinaryNode).Balance() > 0)
                {
                    (node.Children.First() as BinaryNode).LeftRotation();
                }
                return node.RightRotation();
            }

            return node;
        }
    }
}
