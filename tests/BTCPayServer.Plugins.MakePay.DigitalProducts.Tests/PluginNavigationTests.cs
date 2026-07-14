#nullable enable
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class PluginNavigationTests
{
    [Fact]
    public void Store_navigation_uses_the_btcpay_product_sprite_icon()
    {
        var source = File.ReadAllText(CustomDomainGuideTests.RepositoryFile(
            "src",
            "BTCPayServer.Plugins.MakePay.DigitalProducts",
            "Views",
            "Shared",
            "DigitalDownloads",
            "StoreNavExtension.cshtml"));

        Assert.Contains("<vc:icon symbol=\"nav-products\" />", source, StringComparison.Ordinal);
        Assert.Contains("layout-menu-item=\"DigitalDownloads\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", source, StringComparison.OrdinalIgnoreCase);
    }
}
