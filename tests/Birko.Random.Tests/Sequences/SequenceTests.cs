using Birko.Random;
using FluentAssertions;
using Xunit;

namespace Birko.Random.Tests.Sequences;

public class GuidGeneratorTests
{
    [Fact]
    public void NewGuidV4_ReturnsUniqueGuids()
    {
        var guids = new HashSet<Guid>();

        for (int i = 0; i < 100; i++)
        {
            guids.Add(GuidGenerator.NewGuidV4()).Should().BeTrue();
        }
    }

    [Fact]
    public void NewGuidV4_HasVersion4()
    {
        var guid = GuidGenerator.NewGuidV4();
        var bytes = guid.ToByteArray(bigEndian: true);

        // Version nibble (byte 6, high nibble) should be 4
        ((bytes[6] >> 4) & 0x0F).Should().Be(4);
    }

    [Fact]
    public void NewGuidV4_HasVariant1()
    {
        var guid = GuidGenerator.NewGuidV4();
        var bytes = guid.ToByteArray(bigEndian: true);

        // Variant (byte 8, high 2 bits) should be 10
        ((bytes[8] >> 6) & 0x03).Should().Be(2);
    }

    [Fact]
    public void NewGuidV7_ReturnsUniqueGuids()
    {
        var guids = new HashSet<Guid>();

        for (int i = 0; i < 100; i++)
        {
            guids.Add(GuidGenerator.NewGuidV7()).Should().BeTrue();
        }
    }

    [Fact]
    public void NewGuidV7_HasVersion7()
    {
        var guid = GuidGenerator.NewGuidV7();
        var bytes = guid.ToByteArray(bigEndian: true);

        ((bytes[6] >> 4) & 0x0F).Should().Be(7);
    }

    [Fact]
    public void NewGuidV7_IsTimeSorted()
    {
        var earlier = GuidGenerator.NewGuidV7(DateTimeOffset.UtcNow.AddSeconds(-10));
        var later = GuidGenerator.NewGuidV7(DateTimeOffset.UtcNow);

        // V7 GUIDs created at different times should sort by time
        string.Compare(earlier.ToString(), later.ToString(), StringComparison.Ordinal)
            .Should().BeLessThan(0);
    }

    [Fact]
    public void NewGuidV7_WithTimestamp_EncodesCorrectTime()
    {
        var timestamp = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var guid = GuidGenerator.NewGuidV7(timestamp);
        var bytes = guid.ToByteArray(bigEndian: true);

        // Extract first 48 bits as Unix ms
        long extractedMs = ((long)bytes[0] << 40) | ((long)bytes[1] << 32) |
                           ((long)bytes[2] << 24) | ((long)bytes[3] << 16) |
                           ((long)bytes[4] << 8) | bytes[5];

        extractedMs.Should().Be(timestamp.ToUnixTimeMilliseconds());
    }
}

public class NanoIdGeneratorTests
{
    [Fact]
    public void New_ReturnsDefaultLength()
    {
        var id = NanoIdGenerator.New();

        id.Should().HaveLength(NanoIdGenerator.DefaultSize);
    }

    [Fact]
    public void New_ReturnsUniqueValues()
    {
        var ids = new HashSet<string>();

        for (int i = 0; i < 100; i++)
        {
            ids.Add(NanoIdGenerator.New()).Should().BeTrue();
        }
    }

    [Fact]
    public void New_WithCustomSize_ReturnsCorrectLength()
    {
        var id = NanoIdGenerator.New(10);

        id.Should().HaveLength(10);
    }

    [Fact]
    public void New_ContainsOnlyDefaultAlphabet()
    {
        var id = NanoIdGenerator.New();

        id.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void New_WithCustomAlphabet_UsesOnlyThoseChars()
    {
        var id = NanoIdGenerator.New("abc", 20);

        id.Should().MatchRegex("^[abc]+$");
        id.Should().HaveLength(20);
    }

    [Fact]
    public void New_EmptyAlphabet_Throws()
    {
        var act = () => NanoIdGenerator.New("", 10);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void New_InvalidSize_Throws()
    {
        var act = () => NanoIdGenerator.New(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void New_WithProvider_UsesProvider()
    {
        var provider = new TestRandomProvider();
        // Enqueue enough bytes to generate a short NanoID
        provider.EnqueueBytes(
            new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
                         0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 });

        var id = NanoIdGenerator.New(provider, "abcd", 3);

        id.Should().HaveLength(3);
        id.Should().MatchRegex("^[abcd]+$");
    }
}

public class SnowflakeGeneratorTests
{
    [Fact]
    public void Next_ReturnsUniqueIds()
    {
        var gen = new SnowflakeGenerator(1);
        var ids = new HashSet<long>();

        for (int i = 0; i < 100; i++)
        {
            ids.Add(gen.Next()).Should().BeTrue();
        }
    }

    [Fact]
    public void Next_ReturnsPositiveValues()
    {
        var gen = new SnowflakeGenerator(0);

        for (int i = 0; i < 100; i++)
        {
            gen.Next().Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Next_IsMonotonicallyIncreasing()
    {
        var gen = new SnowflakeGenerator(1);
        long prev = 0;

        for (int i = 0; i < 100; i++)
        {
            long current = gen.Next();
            current.Should().BeGreaterThan(prev);
            prev = current;
        }
    }

    [Fact]
    public void ExtractMachineId_ReturnsCorrectId()
    {
        var gen = new SnowflakeGenerator(42);
        long id = gen.Next();

        SnowflakeGenerator.ExtractMachineId(id).Should().Be(42);
    }

    [Fact]
    public void ExtractTimestamp_ReturnsRecentTime()
    {
        var gen = new SnowflakeGenerator(1);
        var before = DateTimeOffset.UtcNow;
        long id = gen.Next();
        var after = DateTimeOffset.UtcNow;

        var timestamp = gen.ExtractTimestamp(id);
        timestamp.Should().BeOnOrAfter(before.AddMilliseconds(-1));
        timestamp.Should().BeOnOrBefore(after.AddMilliseconds(1));
    }

    [Fact]
    public void Constructor_InvalidMachineId_Throws()
    {
        var act1 = () => new SnowflakeGenerator(-1);
        var act2 = () => new SnowflakeGenerator(1024);

        act1.Should().Throw<ArgumentOutOfRangeException>();
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public class TokenGeneratorTests
{
    [Fact]
    public void NewHex_ReturnsCorrectLength()
    {
        var token = TokenGenerator.NewHex(16);

        token.Should().HaveLength(32); // 16 bytes = 32 hex chars
    }

    [Fact]
    public void NewHex_ContainsOnlyHexChars()
    {
        var token = TokenGenerator.NewHex();

        token.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void NewBase64Url_ContainsNoUnsafeChars()
    {
        var token = TokenGenerator.NewBase64Url();

        token.Should().NotContain("+");
        token.Should().NotContain("/");
        token.Should().NotContain("=");
    }

    [Fact]
    public void NewUrlSafe_ReturnsCorrectLength()
    {
        var token = TokenGenerator.NewUrlSafe(20);

        token.Should().HaveLength(20);
    }

    [Fact]
    public void NewApiKey_HasCorrectPrefix()
    {
        var key = TokenGenerator.NewApiKey("sk_live");

        key.Should().StartWith("sk_live_");
    }

    [Fact]
    public void NewApiKey_ReturnsUniqueValues()
    {
        var keys = new HashSet<string>();

        for (int i = 0; i < 100; i++)
        {
            keys.Add(TokenGenerator.NewApiKey("test")).Should().BeTrue();
        }
    }

    [Fact]
    public void NewHex_InvalidLength_Throws()
    {
        var act = () => TokenGenerator.NewHex(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NewApiKey_EmptyPrefix_Throws()
    {
        var act = () => TokenGenerator.NewApiKey("");

        act.Should().Throw<ArgumentException>();
    }
}
