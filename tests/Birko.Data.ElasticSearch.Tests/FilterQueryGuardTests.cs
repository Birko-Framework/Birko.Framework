using Birko.Data.ElasticSearch.Tests.TestResources.Models;
using FluentAssertions;
using Nest;
using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests
{
    /// <summary>
    /// CR-H047 — a filter that was SUPPLIED but cannot be translated must never silently widen to
    /// match-all. <c>ParseExpression</c> returns <c>null</c> for shapes it cannot express, and a NEST
    /// request with <c>Query = null</c> carries no query, which ElasticSearch reads as match-all: a
    /// filtered / existence / permission check quietly becomes a full-result set. On
    /// <c>_delete_by_query</c> / <c>_update_by_query</c> the same null is worse than a wrong read.
    ///
    /// The invariant used to be enforced in <c>ElasticSearchViewStore.BuildFilterQuery</c> only, while the
    /// main entity stores assigned the parser output straight to their requests (14 sites, 0 guards) —
    /// TASK-268. It now lives in <c>ParseFilterQuery</c> / <c>ParseRequiredFilterQuery</c>, which every
    /// filter→query conversion routes through.
    ///
    /// Three outcomes are deliberately distinct, and only one is an error:
    ///   · no filter supplied         → null query, read everything ON PURPOSE
    ///   · filter matching nothing    → MatchNoneQuery (TASK-266), a legitimate translation
    ///   · filter that cannot express → throw
    /// </summary>
    public class FilterQueryGuardTests
    {
        // Genuinely untranslatable, verified by probing the parser rather than assumed: `Trim()` is not in
        // ParseMethodCall's supported set and is parameter-dependent, so it cannot be constant-folded.
        // NOTE: `.Length` and a boolean ternary are NOT untranslatable — Length resolves and STORY-047's
        // ExpressionNormalizer desugars ternaries. Picking either as an "untranslatable" example is the
        // mistake that left the original CR-H047 guard test red (TASK-267).
        private static readonly Expression<Func<DateModel, bool>> Untranslatable = x => x.Text.Trim() == "a";

        [Fact]
        public void Untranslatable_IsStillNullFromTheRawParser()
        {
            // Pins the premise of every test below. If the parser ever learns to translate `Trim()`, these
            // tests would silently stop exercising the guard — this assertion fails loudly instead.
            Birko.Data.ElasticSearch.ElasticSearch.ParseExpression(Untranslatable).Should().BeNull();
        }

        // ── ParseFilterQuery: optional filter ──────────────────────────────────────────────────────────

        [Fact]
        public void NullFilter_ReturnsNull_SoTheCallerCanReadEverythingOnPurpose()
        {
            Birko.Data.ElasticSearch.ElasticSearch
                .ParseFilterQuery<DateModel>(null)
                .Should().BeNull("no filter supplied is not the same as a filter that failed to translate");
        }

        [Fact]
        public void TranslatableFilter_ReturnsTheQuery()
        {
            Birko.Data.ElasticSearch.ElasticSearch
                .ParseFilterQuery<DateModel>(x => x.Count == 5)
                .Should().BeOfType<TermQuery>();
        }

        [Fact]
        public void UntranslatableFilter_Throws_DoesNotWidenToMatchAll()
        {
            var act = () => Birko.Data.ElasticSearch.ElasticSearch.ParseFilterQuery(Untranslatable);

            act.Should().Throw<NotSupportedException>()
                .WithMessage("*could not be translated*");
        }

        [Fact]
        public void FilterThatLegitimatelyMatchesNothing_DoesNotThrow()
        {
            // The TASK-266 distinction: an empty collection is translatable — it means "matches nothing",
            // which is a real answer, not a failure. Guards against a lazy fix that threw on any falsy
            // query and thereby broke empty-batch reads.
            var ids = Array.Empty<int>();

            Birko.Data.ElasticSearch.ElasticSearch
                .ParseFilterQuery<DateModel>(x => ids.Contains(x.Count))
                .Should().BeOfType<MatchNoneQuery>();
        }

        // ── ParseRequiredFilterQuery: the destructive by-query paths ───────────────────────────────────

        [Fact]
        public void RequiredFilter_Untranslatable_Throws()
        {
            var act = () => Birko.Data.ElasticSearch.ElasticSearch.ParseRequiredFilterQuery(Untranslatable);

            act.Should().Throw<NotSupportedException>();
        }

        [Fact]
        public void RequiredFilter_Null_Throws_RatherThanTargetingEveryDocument()
        {
            // A by-query delete/update with no query would target the whole index. The signature is
            // non-nullable, so this is a contract violation — but it must fail loudly, not delete everything.
            var act = () => Birko.Data.ElasticSearch.ElasticSearch.ParseRequiredFilterQuery<DateModel>(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void RequiredFilter_Translatable_ReturnsTheQuery()
        {
            Birko.Data.ElasticSearch.ElasticSearch
                .ParseRequiredFilterQuery<DateModel>(x => x.Count == 5)
                .Should().BeOfType<TermQuery>();
        }

        // ── Structural: no store path may bypass the guard ─────────────────────────────────────────────

        /// <summary>
        /// The "a new call site cannot forget it" guarantee. Guarding 14 call sites by hand is only as good
        /// as the next person remembering; this fails if any store assigns the RAW parser output to a
        /// request's Query again. Source-scan rather than reflection, because method bodies are not
        /// inspectable without a decompiler.
        /// </summary>
        [Fact]
        public void NoStorePath_AssignsRawParseExpression_ToARequestQuery()
        {
            var storesDir = FindStoresDirectory();
            if (storesDir == null)
            {
                // Graceful skip: the framework source is not laid out beside this repo (e.g. a package-only
                // consumer). The behavioural tests above still hold; only this structural check needs source.
                return;
            }

            var offenders = Directory.GetFiles(storesDir, "*.cs")
                .SelectMany(f => File.ReadAllLines(f)
                    .Select((line, i) => (file: Path.GetFileName(f), no: i + 1, line))
                    .Where(l => l.line.Contains("ParseExpression(")
                             && (l.line.Contains("Query =") || l.line.Contains("var query ="))))
                .Select(l => $"{l.file}:{l.no} → {l.line.Trim()}")
                .ToArray();

            offenders.Should().BeEmpty(
                "every filter→query conversion must go through ParseFilterQuery / ParseRequiredFilterQuery "
                + "so CR-H047 cannot be bypassed by a new call site");
        }

        private static string? FindStoresDirectory()
        {
            // …/Framework.Tests/Birko.Data.ElasticSearch.Tests/bin/Debug/net10.0 → walk up to the repo root,
            // then across to the source project.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Framework", "Birko.Data.ElasticSearch", "Stores");
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
