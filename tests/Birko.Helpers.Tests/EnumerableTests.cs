using System;
using System.Linq;
using Birko.Helpers;
using Xunit;

namespace Birko.Helpers.Tests
{
    public class EnumerableTests
    {
        [Fact]
        public void ParseSame()
        {
            int[] source = new[] { 1, 2 };
            var (added, removed, same) = EnumerableHelper.Diff(source, source);
            Assert.Null(added);
            Assert.Null(removed);
            Assert.Equal(2, same?.Count() ?? 0);
            Assert.Equal(string.Join(",", source), string.Join(",", same));
        }

        [Fact]
        public void ParseNone()
        {
            int[] source = new[] { 1, 2 };
            int[] dest = new[] { 3, 4 };
            var (added, removed, same) = EnumerableHelper.Diff(source, dest);
            Assert.Equal(2, added?.Count() ?? 0);
            Assert.Equal(2, removed?.Count() ?? 0);
            Assert.Empty(same);
            Assert.Equal(string.Join(",", dest), string.Join(",", added));
            Assert.Equal(string.Join(",", source), string.Join(",", removed));
        }

        [Fact]
        public void ParseIntercept()
        {
            int[] source = new[] { 1, 2 };
            int[] dest = new[] { 2, 3 };
            var (added, removed, same) = EnumerableHelper.Diff(source, dest);
            Assert.Equal(1, added?.Count() ?? 0);
            Assert.Equal(1, removed?.Count() ?? 0);
            Assert.Equal(1, same?.Count() ?? 0);
            Assert.Equal(string.Join(",", new[] { 3 }), string.Join(",", added));
            Assert.Equal(string.Join(",", new[] { 1 }), string.Join(",", removed));
            Assert.Equal(string.Join(",", new[] { 2 }), string.Join(",", same));
        }

        [Fact]
        public void ParseInterceptString()
        {
            string[] source = new[] { "a", "b" };
            string[] dest = new[] { "B", "c" };
            var (added, removed, same) = EnumerableHelper.Diff(source, dest);
            Assert.Equal(2, added?.Count() ?? 0);
            Assert.Equal(2, removed?.Count() ?? 0);
            Assert.Empty(same);
            Assert.Equal(string.Join(",", new[] { "B", "c" }), string.Join(",", added));
            Assert.Equal(string.Join(",", new[] { "a", "b" }), string.Join(",", removed));
        }

        [Fact]
        public void ParseInterceptStringEqualFunction()
        {
            string[] source = new[] { "a", "b" };
            string[] dest = new[] { "B", "c" };
            var (added, removed, same) = EnumerableHelper.Diff(source, dest, (x, y) => x.Equals(y, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, added?.Count() ?? 0);
            Assert.Equal(1, removed?.Count() ?? 0);
            Assert.Equal(1, same?.Count() ?? 0);
            Assert.Equal(string.Join(",", new[] { "c" }), string.Join(",", added));
            Assert.Equal(string.Join(",", new[] { "a" }), string.Join(",", removed));
            Assert.Equal(string.Join(",", new[] { "b" }), string.Join(",", same));
        }
    }
}
