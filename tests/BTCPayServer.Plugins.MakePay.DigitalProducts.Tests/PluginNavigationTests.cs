#nullable enable
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class PluginNavigationTests
{
    [Fact]
    public void Store_navigation_uses_the_dedicated_current_color_product_icon()
    {
        var source = File.ReadAllText(CustomDomainGuideTests.RepositoryFile(
            "src",
            "BTCPayServer.Plugins.MakePay.DigitalProducts",
            "Views",
            "Shared",
            "DigitalDownloads",
            "StoreNavExtension.cshtml"));

        Assert.Contains("class=\"icon icon-makepay-digital-products\"", source, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 24 24\"", source, StringComparison.Ordinal);
        Assert.Contains("currentColor", source, StringComparison.Ordinal);
        Assert.Contains("aria-hidden=\"true\"", source, StringComparison.Ordinal);
        Assert.Contains("layout-menu-item=\"DigitalDownloads\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<vc:icon", source, StringComparison.OrdinalIgnoreCase);
    }
}
