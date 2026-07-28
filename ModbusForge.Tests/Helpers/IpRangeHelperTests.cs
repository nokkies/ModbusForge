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
}
