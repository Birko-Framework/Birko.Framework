using System.Globalization;
using Birko.Data.Localization.Models;

namespace Birko.Data.Localization.Tests.TestResources;

public class TestEntityLocalizationContext : IEntityLocalizationContext
{
    public CultureInfo CurrentCulture { get; set; } = new CultureInfo("en");
    public CultureInfo DefaultCulture { get; set; } = new CultureInfo("en");
}
