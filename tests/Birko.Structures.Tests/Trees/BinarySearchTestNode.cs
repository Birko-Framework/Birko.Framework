using Birko.Structures.Trees;
using System;

namespace Birko.Structures.Tests.Trees
{
    internal class BinarySearchTestNode : BinarySearchNode
    {
        public int Data { get; set; }

        public override int CompareTo(Node? other)
        {
            if (other is not BinarySearchTestNode testNode)
            {
                throw new ArgumentException("Argument is not a instance of BinarySearchTestNode");
            }
            return Data.CompareTo(testNode.Data);
        }

        public override string ToString()
        {
            return Data.ToString();
        }
    }
}
