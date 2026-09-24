using System;
using ModbusForge.Helpers;
using Xunit;

namespace ModbusForge.Tests.Helpers;

public class IpRangeHelperTests
{
    [Fact]
    public void Expand_SingleAddress_ReturnsThatAddress()
    {
        var addresses = IpRangeHelper.Expand("192.168.1.10", "192.168.1.10");

        Assert.Equal(new[] { "192.168.1.10" }, addresses);
    }

    [Fact]
    public void Expand_AcrossOctetBoundary_ReturnsEveryAddress()
    {
        var addresses = IpRangeHelper.Expand("10.0.0.254", "10.0.1.1");

        Assert.Equal(new[] { "10.0.0.254", "10.0.0.255", "10.0.1.0", "10.0.1.1" }, addresses);
    }

    [Fact]
    public void Expand_ReversedRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => IpRangeHelper.Expand("192.168.1.20", "192.168.1.10"));
    }

    [Fact]
    public void Expand_TooManyAddresses_Throws()
    {
        Assert.Throws<ArgumentException>(() => IpRangeHelper.Expand("10.0.0.0", "10.0.255.255"));
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("")]
    [InlineData("::1")]
    public void Expand_InvalidAddress_Throws(string address)
    {
        Assert.Throws<ArgumentException>(() => IpRangeHelper.Expand(address, "10.0.0.1"));
    }

    [Fact]
    public void TryParseIPv4_RoundTripsThroughToIPv4String()
    {
        Assert.True(IpRangeHelper.TryParseIPv4("172.16.254.1", out var value));
        Assert.Equal("172.16.254.1", IpRangeHelper.ToIPv4String(value));
    }

    [Theory]
    [InlineData(new object?[] { null })]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void TryParseIPv4_NullOrWhitespace_ReturnsFalseAndZero(string? address)
    {
        var result = IpRangeHelper.TryParseIPv4(address, out var value);

        Assert.False(result);
        Assert.Equal(0u, value);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("256.1.1.1")]
    [InlineData("192.168.1.256")]
    [InlineData("192.168.1.1.1")]
    [InlineData("192.168.1.abc")]
    [InlineData("192.168.1.-1")]
    public void TryParseIPv4_InvalidFormat_ReturnsFalseAndZero(string address)
    {
        var result = IpRangeHelper.TryParseIPv4(address, out var value);

        Assert.False(result);
        Assert.Equal(0u, value);
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334")]
    public void TryParseIPv4_IPv6Address_ReturnsFalseAndZero(string address)
    {
        var result = IpRangeHelper.TryParseIPv4(address, out var value);

        Assert.False(result);
        Assert.Equal(0u, value);
    }

    [Theory]
    [InlineData(" 192.168.1.10 ", 3232235786u)]
    [InlineData("\t10.0.0.1\n", 167772161u)]
    public void TryParseIPv4_ValidAddressWithWhitespace_ReturnsTrueAndCorrectValue(string address, uint expectedValue)
    {
        var result = IpRangeHelper.TryParseIPv4(address, out var value);

        Assert.True(result);
        Assert.Equal(expectedValue, value);
    }

    [Theory]
    [InlineData("0.0.0.0", 0u)]
    [InlineData("255.255.255.255", 4294967295u)]
    [InlineData("127.0.0.1", 2130706433u)]
    [InlineData("192.168.1.1", 3232235777u)]
    [InlineData("10.0.0.255", 167772415u)]
    public void TryParseIPv4_ValidAddress_ReturnsTrueAndCorrectValue(string address, uint expectedValue)
    {
        var result = IpRangeHelper.TryParseIPv4(address, out var value);

        Assert.True(result);
        Assert.Equal(expectedValue, value);
    }

    [Theory]
    [InlineData(0u, "0.0.0.0")]
    [InlineData(4294967295u, "255.255.255.255")]
    [InlineData(2130706433u, "127.0.0.1")]
    [InlineData(3232235777u, "192.168.1.1")]
    public void ToIPv4String_ValidUint_ReturnsCorrectIpString(uint value, string expectedIp)
    {
        var result = IpRangeHelper.ToIPv4String(value);

        Assert.Equal(expectedIp, result);
    }
}
