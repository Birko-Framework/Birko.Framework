using System;
using System.IO;
using System.Linq;
using Birko.Data.Repositories;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Repositories.Tests;

/// <summary>
/// SH-H046, widened — <c>IBaseRepository.Destroy()</c> / <c>IAsyncBaseRepository.DestroyAsync()</c>
/// carried the same disposal wording as the store contract, and forward straight to it.
/// </summary>
/// <remarks>
/// <para>
/// The finding named `IStore` only. The repository interfaces read <i>"Destroys the repository and
/// releases all resources"</i> — the same sentence — and every family (<c>AbstractRepository</c>,
/// <c>AbstractAsyncRepository</c>, <c>AbstractViewModelRepository</c>,
/// <c>AbstractAsyncViewModelRepository</c>) forwards to the store's <c>Destroy</c>. So this is not a
/// milder operation one layer up: it is the same destruction one call away, and on a SQL-backed
/// repository it drops the entity's table. Warning on one contract while reassuring on the other is
/// the half-fix CLAUDE.md § TASK-215 forbids — <i>guard the whole verb family or none of it</i>.
/// </para>
/// <para>
/// ⚠ Only the <b>documentation</b> is pinned here. The behaviour already has coverage in
/// <c>AsyncBulkRepositoryDestroyTests</c>, and duplicating it would be noise — the same judgement the
/// store-side file records for <c>InMemoryStoreTests.Destroy_ShouldClearAllData</c>.
/// </para>
/// </remarks>
public class DestroyIsNotDisposalDocTests
{
    [Fact]
    public void The_repository_contract_does_not_describe_Destroy_as_releasing_resources()
    {
        var source = File.ReadAllText(Path.Combine(RepositoriesSourceDirectory(), "IBaseRepository.cs"));

        // ⚠ Scan the prose with quotations stripped: the new remarks quote the old wording to record
        // what the member used to claim, so a flat NotContain matches the explanation and fails for
        // the wrong reason. Measured three times this session, on three different scans.
        WithoutQuotations(source).Should().NotContain("releases all resources",
            "SH-H046: both repository members forward to the store's Destroy, so describing them as "
            + "resource release is the same lie one layer up");

        source.Split("PERMANENTLY DELETES").Length.Should().Be(3,
            "both Destroy() and DestroyAsync(ct) must lead with what they destroy — fixing one and "
            + "leaving the other is the asymmetry this finding is about");
    }

    [Fact]
    public void The_repository_contract_declares_no_disposal_member_so_the_redirect_is_accurate()
    {
        // The new doc redirects resource release to the underlying store's IDisposable. That is only
        // honest while the repository contract itself has none; if one is added, this fails and the
        // wording must be revisited rather than left stale.
        foreach (var contract in new[] { typeof(IBaseRepository), typeof(IAsyncBaseRepository) })
        {
            typeof(IDisposable).IsAssignableFrom(contract).Should().BeFalse(
                $"{contract.Name} offers no disposal member, which is why Destroy() was the only "
                + "cleanup-looking thing a caller could find here");
        }
    }

    /// <summary>
    /// Removes every <c>&lt;i&gt;…&lt;/i&gt;</c> span so the scan reads the prose that DESCRIBES the
    /// member, not the prose that QUOTES what it used to claim.
    /// </summary>
    /// <remarks>
    /// ⚠ A line-based filter is not enough, and the first version of this used one: the async remark
    /// wraps its quotation across two lines, so the continuation line carries the phrase with no
    /// opening tag on it and the scan failed for the wrong reason. Spans are stripped across newlines.
    /// </remarks>
    private static string WithoutQuotations(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        int i = 0;
        while (i < source.Length)
        {
            int open = source.IndexOf("<i>", i, StringComparison.Ordinal);
            if (open < 0)
            {
                result.Append(source, i, source.Length - i);
                break;
            }

            result.Append(source, i, open - i);
            int close = source.IndexOf("</i>", open, StringComparison.Ordinal);
            i = close < 0 ? source.Length : close + "</i>".Length;
        }

        return result.ToString();
    }

    private static string RepositoriesSourceDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            foreach (var root in new[] { dir.FullName, Path.Combine(dir.FullName, "Framework") })
            {
                var path = Path.Combine(root, "Birko.Data.Repositories");
                if (Directory.Exists(path))
                {
                    return path;
                }
            }
        }

        throw new DirectoryNotFoundException(
            "Birko.Data.Repositories was not findable from the test binary; this scan is the only "
            + "thing stopping the wording drifting back, so do not weaken it into a silent skip.");
    }
}
