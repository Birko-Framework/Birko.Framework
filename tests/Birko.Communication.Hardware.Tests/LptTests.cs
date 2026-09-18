using Birko.Communication.Hardware.Ports;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Communication.Hardware.Tests;

/// <summary>
/// Regression tests for CR-C03: LPT.Write/Read passed the logical LPT number (1/2/3) to inpout32's
/// Out32/Inp32, which interpret their first argument as the absolute I/O port ADDRESS. The logical
/// number must be mapped to the parallel-port base address first. (The P/Invoke itself can't be
/// exercised without hardware; the address mapping is the testable unit.)
/// </summary>
public class LptTests
{
    [Theory]
    [InlineData(1, 0x378)]
    [InlineData(2, 0x278)]
    [InlineData(3, 0x3BC)]
    public void ResolvePortAddress_MapsLogicalNumberToBaseAddress(int number, int expected)
    {
        LPT.ResolvePortAddress(number).Should().Be(expected);
    }

    [Fact]
    public void ResolvePortAddress_PassesThroughRawAddress()
    {
        // A caller may specify a non-standard raw address directly.
        LPT.ResolvePortAddress(0x378).Should().Be(0x378);
        LPT.ResolvePortAddress(0x3BC).Should().Be(0x3BC);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(99)]
    public void ResolvePortAddress_RejectsAmbiguousSmallValues(int number)
    {
        Action act = () => LPT.ResolvePortAddress(number);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
