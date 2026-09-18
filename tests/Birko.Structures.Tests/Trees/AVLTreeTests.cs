using Birko.Structures.Trees;
using FluentAssertions;
using Xunit;

namespace Birko.Structures.Tests.Trees;

public class AVLTreeTests
{
    [Fact]
    public void InOrder_ReturnsSorted()
    {
        var tree = new AVLTree<int>(new[] { 9, 5, 2, 3, 1, 4, 8, 6, 7 });

        var inOrder = tree.InOrder().ToList();
        inOrder.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Fact]
    public void Height_StaysBalanced()
    {
        var tree = new AVLTree<int>();
        for (int i = 1; i <= 15; i++)
        {
            tree.Insert(i);
        }

        tree.Height.Should().BeLessThanOrEqualTo(5);
        tree.Count.Should().Be(15);
    }

    [Fact]
    public void Remove_MaintainsBalance()
    {
        var tree = new AVLTree<int>(new[] { 5, 3, 7, 1, 4, 6, 8 });
        tree.Remove(1);
        tree.Remove(3);

        tree.InOrder().Should().Equal(4, 5, 6, 7, 8);
        tree.Height.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public void LeftHeavy_RightRotation()
    {
        var tree = new AVLTree<int>();
        tree.Insert(3);
        tree.Insert(2);
        tree.Insert(1);

        tree.Root!.Value.Should().Be(2);
        tree.InOrder().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void RightHeavy_LeftRotation()
    {
        var tree = new AVLTree<int>();
        tree.Insert(1);
        tree.Insert(2);
        tree.Insert(3);

        tree.Root!.Value.Should().Be(2);
        tree.InOrder().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Contains_And_Find()
    {
        var tree = new AVLTree<int>(new[] { 10, 20, 30, 40, 50 });
        tree.Contains(30).Should().BeTrue();
        tree.Contains(99).Should().BeFalse();
        tree.Find(40)!.Value.Should().Be(40);
    }
}

public class TreeTests
{
    [Fact]
    public void GenericTree_AddChildren()
    {
        var tree = new Tree<string>("root");
        var child1 = tree.Root!.AddChild("child1");
        var child2 = tree.Root.AddChild("child2");
        child1.AddChild("grandchild");

        tree.Count.Should().Be(4);
        tree.Height.Should().Be(3);
    }

    [Fact]
    public void GenericTree_PreOrder()
    {
        var tree = new Tree<int>(1);
        var left = tree.Root!.AddChild(2);
        var right = tree.Root.AddChild(3);
        left.AddChild(4);

        tree.PreOrder().Should().Equal(1, 2, 4, 3);
    }

    [Fact]
    public void GenericTree_LevelOrder()
    {
        var tree = new Tree<int>(1);
        tree.Root!.AddChild(2);
        tree.Root.AddChild(3);

        tree.LevelOrder().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void GenericTree_Find()
    {
        var tree = new Tree<string>("a");
        tree.Root!.AddChild("b").AddChild("c");

        tree.Find("c").Should().NotBeNull();
        tree.Find("z").Should().BeNull();
    }
}
