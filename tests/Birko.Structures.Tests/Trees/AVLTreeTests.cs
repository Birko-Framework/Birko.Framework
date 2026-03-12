using Birko.Structures.Extensions.Trees;
using Birko.Structures.Trees;
using Xunit;

namespace Birko.Structures.Tests.Trees
{
    public class AVLTreeTests
    {
        public static AVLTree testTree = new(new[] {
            new BinarySearchTestNode() { Data = 9 },
            new BinarySearchTestNode() { Data = 5 },
            new BinarySearchTestNode() { Data = 2 },
            new BinarySearchTestNode() { Data = 3 },
            new BinarySearchTestNode() { Data = 1 },
            new BinarySearchTestNode() { Data = 4 },
            new BinarySearchTestNode() { Data = 8 },
            new BinarySearchTestNode() { Data = 6 },
            new BinarySearchTestNode() { Data = 7 },
        });

        [Fact]
        public void TestInOrder()
        {
            string treePath = string.Join("/", testTree.InOrder());
            string testPath = string.Join("/", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            Assert.Equal(testPath, treePath);
        }
    }
}
