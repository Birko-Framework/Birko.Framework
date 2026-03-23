using Birko.Structures.Trees;
using FluentAssertions;
using Xunit;

namespace Birko.Structures.Tests.Trees;

public class BinarySearchTreeTests
{
    [Fact]
    public void InOrder_ReturnsSorted()
    {
        var tree = new BinarySearchTree<int>(new[] { 9, 5, 2, 3, 1, 4, 8, 6, 7 });

        var inOrder = tree.InOrder().ToList();
        inOrder.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Fact]
    public void Insert_And_Find()
    {
        var tree = new BinarySearchTree<int>();
        tree.Insert(5);
        tree.Insert(3);
        tree.Insert(7);

        tree.Find(3).Should().NotBeNull();
        tree.Find(3)!.Value.Should().Be(3);
        tree.Find(99).Should().BeNull();
    }

    [Fact]
    public void Remove_MaintainsBstProperty()
    {
        var tree = new BinarySearchTree<int>(new[] { 5, 3, 7, 1, 4, 6, 8 });
        tree.Remove(5).Should().BeTrue();
        tree.Contains(5).Should().BeFalse();
        tree.InOrder().Should().Equal(1, 3, 4, 6, 7, 8);
    }

    [Fact]
    public void Min_And_Max()
    {
        var tree = new BinarySearchTree<int>(new[] { 5, 3, 7, 1, 9 });
        tree.Min().Should().Be(1);
        tree.Max().Should().Be(9);
    }

    [Fact]
    public void Count_TracksInsertAndRemove()
    {
        var tree = new BinarySearchTree<int>();
        tree.Insert(5); tree.Insert(3); tree.Insert(7);
        tree.Count.Should().Be(3);
        tree.Remove(3);
        tree.Count.Should().Be(2);
    }

    [Fact]
    public void PreOrder_And_PostOrder()
    {
        var tree = new BinarySearchTree<int>(new[] { 5, 3, 7 });
        tree.PreOrder().Should().Equal(5, 3, 7);
        tree.PostOrder().Should().Equal(3, 7, 5);
    }

    [Fact]
    public void LevelOrder_ReturnsBFS()
    {
        var tree = new BinarySearchTree<int>(new[] { 5, 3, 7, 1, 4 });
        tree.LevelOrder().Should().Equal(5, 3, 7, 1, 4);
    }

    [Fact]
    public void WorksWithStrings()
    {
        var tree = new BinarySearchTree<string>(new[] { "banana", "apple", "cherry" });
        tree.InOrder().Should().Equal("apple", "banana", "cherry");
    }
}
