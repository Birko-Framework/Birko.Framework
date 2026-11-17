using System;
using Birko.Helpers;
using FluentAssertions;
using Xunit;

namespace Birko.Helpers.Tests;

/// <summary>TASK-811: a store's file is its template with exactly one key in place of the <c>*</c>.</summary>
public class RecordFilesTests
{
    private static readonly string G = Guid.NewGuid().ToString("D");

    [Theory]
    [InlineData("items-*", "items-{g}", true)]
    [InlineData("items-*", "items-archive-{g}", false)]
    [InlineData("items-*", "items-{g}-{g}", false)]
    [InlineData("items-*", "items-", false)]
    [InlineData("items-*.xml", "items-{g}.xml", true)]
    [InlineData("items-*.xml", "items-archive-{g}.xml", false)]
    [InlineData("pre*post", "pre{g}post", true)]
    [InlineData("pre*post", "prex{g}post", false)]
    public void A_file_belongs_only_with_one_guid_in_place_of_the_star(string template, string file, bool belongs)
    {
        RecordFiles.Belongs(file.Replace("{g}", G), template, RecordFiles.IsGuid).Should().Be(belongs);
    }

    [Theory]
    [InlineData("items-3.xml", true)]
    [InlineData("items-12.xml", true)]
    [InlineData("items-archive-1.xml", false)]
    [InlineData("items-.xml", false)]
    public void A_batch_file_belongs_only_with_a_number_in_place_of_the_star(string file, bool belongs)
    {
        RecordFiles.Belongs(file, "items-*.xml", RecordFiles.IsIndex).Should().Be(belongs);
    }
    [Fact]
    public void Batch_files_are_listed_in_batch_order_not_name_order()
    {
        // TASK-931: Directory.GetFiles lists items-10 before items-2 on Windows and in no fixed order on Linux.
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "birko-recordfiles-931-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            foreach (var n in new[] { "10", "2", "0", "1", "11", "007" })
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"items-{n}.xml"), "");
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "items-archive-3.xml"), "");   // not this store's

            RecordFiles.ListByIndex(dir, "items-*.xml").Select(System.IO.Path.GetFileName)
                .Should().Equal("items-0.xml", "items-1.xml", "items-2.xml", "items-007.xml", "items-10.xml", "items-11.xml");
        }
        finally
        {
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }
}
