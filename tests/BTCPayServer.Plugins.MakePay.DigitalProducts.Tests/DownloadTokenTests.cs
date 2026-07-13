using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using Xunit;
using System.Net;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public class DownloadTokenTests
{
    [Fact]
    public void HashIsStableAndCaseIndependentHex()
    {
        var first = DownloadTokenService.Hash("token-value");
        var second = DownloadTokenService.Hash("token-value");
        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Matches("^[0-9A-F]{64}$", first);
    }

    [Fact]
    public void HashChangesWhenTokenChanges()
    {
        Assert.NotEqual(DownloadTokenService.Hash("token-a"), DownloadTokenService.Hash("token-b"));
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void RemoteOriginAddressPolicy(string value, bool expected) => Assert.Equal(expected, ProductFileService.IsPublicAddress(IPAddress.Parse(value)));

    [Theory]
    [InlineData("Authorization", true)]
    [InlineData("X-Download-Token", true)]
    [InlineData("Host", false)]
    [InlineData("Bad Header", false)]
    public void RemoteHeaderPolicy(string value, bool expected) => Assert.Equal(expected, ProductFileService.IsSafeHeaderName(value));
}
