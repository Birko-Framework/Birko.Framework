using System.Linq;
using System.Threading.Tasks;
using Birko.Data.SQL.Tests.TestResources.Views;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.Views
{
    /// <summary>
    /// CR-H094: DataBase.LoadView cached into a plain static Dictionary mutated without a lock, so
    /// concurrent first-use could corrupt the dictionary. The cache is now a ConcurrentDictionary.
    /// This test hammers LoadView for the same (initially uncached) type from many threads and
    /// asserts it neither throws nor returns divergent results.
    /// </summary>
    public class ViewCacheConcurrencyTests
    {
        [Fact]
        public void ConcurrentLoadView_DoesNotThrow_AndIsConsistent()
        {
            var results = new Birko.Data.SQL.Tables.View[64];

            var act = () => Parallel.For(0, results.Length, i =>
            {
                results[i] = Birko.Data.SQL.DataBase.LoadView(typeof(CustomerOrderView));
            });

            act.Should().NotThrow();
            results.Should().OnlyContain(v => v != null);
            // All calls resolve the same view name (the cache converges on one definition).
            results.Select(v => v.Name).Distinct().Should().ContainSingle();
        }
    }
}
