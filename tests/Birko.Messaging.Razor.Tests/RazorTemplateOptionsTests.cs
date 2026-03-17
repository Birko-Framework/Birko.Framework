using System.Text;
using Birko.Messaging.Razor;
using FluentAssertions;
using Xunit;

namespace Birko.Messaging.Razor.Tests;

public class RazorTemplateOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new RazorTemplateOptions();

        options.TemplateBasePath.Should().BeNull();
        options.FileExtension.Should().Be(".cshtml");
        options.EnableCaching.Should().BeTrue();
        options.FileEncoding.Should().Be(Encoding.UTF8);
        options.DefaultNamespaces.Should().BeEmpty();
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var options = new RazorTemplateOptions
        {
            TemplateBasePath = "/templates",
            FileExtension = ".razor",
            EnableCaching = false,
            FileEncoding = Encoding.ASCII,
            DefaultNamespaces = new[] { "System.Linq", "System.Collections.Generic" }
        };

        options.TemplateBasePath.Should().Be("/templates");
        options.FileExtension.Should().Be(".razor");
        options.EnableCaching.Should().BeFalse();
        options.FileEncoding.Should().Be(Encoding.ASCII);
        options.DefaultNamespaces.Should().HaveCount(2);
    }
}
