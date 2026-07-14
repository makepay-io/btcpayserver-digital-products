#nullable enable
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class StorefrontInteractionTests
{
    [Fact]
    public void Add_to_cart_uses_background_json_with_native_form_fallback_and_one_analytics_source()
    {
        var storefront = Source("Views", "DigitalDownloads", "Public", "Storefront.cshtml");
        var product = Source("Views", "DigitalDownloads", "Public", "Product.cshtml");
        var controller = Source("Controllers", "DigitalDownloadsPublicController.cs");
        var assets = Source("Controllers", "DigitalDownloadsAssetsController.cs");
        var runtime = Source("Assets", "makepay-cart.js");

        Assert.All(new[] { storefront, product }, view =>
        {
            Assert.Contains("data-makepay-cart-form=\"true\"", view, StringComparison.Ordinal);
            Assert.Contains("data-makepay-analytics-form=\"add_to_cart\"", view, StringComparison.Ordinal);
            Assert.Contains("data-makepay-cart-trigger", view, StringComparison.Ordinal);
            Assert.Contains("data-makepay-cart-count", view, StringComparison.Ordinal);
            Assert.Contains("makepay-cart.js", view, StringComparison.Ordinal);
        });

        Assert.Contains("if (WantsJsonResponse())", controller, StringComparison.Ordinal);
        Assert.Contains("cartCount = cart.Lines.Sum", controller, StringComparison.Ordinal);
        Assert.Contains("cartUrl = PublicAction(storeId, nameof(CartPage))", controller, StringComparison.Ordinal);
        Assert.Contains("return Redirect(LocalReturn(returnUrl", controller, StringComparison.Ordinal);
        Assert.Contains("[\"makepay-cart.js\"]", assets, StringComparison.Ordinal);

        Assert.Contains("event.preventDefault()", runtime, StringComparison.Ordinal);
        Assert.Contains("await fetch(form.action", runtime, StringComparison.Ordinal);
        Assert.Contains("Accept: 'application/json'", runtime, StringComparison.Ordinal);
        Assert.Contains("credentials: 'same-origin'", runtime, StringComparison.Ordinal);
        Assert.Contains("HTMLFormElement.prototype.submit.call(form)", runtime, StringComparison.Ordinal);
        Assert.Contains("setTimeout(() =>", runtime, StringComparison.Ordinal);
        Assert.Contains("}, 6500)", runtime, StringComparison.Ordinal);
        Assert.Contains("pointerenter", runtime, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", runtime, StringComparison.Ordinal);
        Assert.Contains("Go to cart", runtime, StringComparison.Ordinal);
        Assert.Contains("Continue browsing", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("makePayAnalytics.track", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("dispatchEvent(new Event('submit'", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Hero_slides_share_one_grid_track_at_every_breakpoint()
    {
        var storefront = Source("Views", "DigitalDownloads", "Public", "Storefront.cshtml");

        Assert.Contains(".dp-market-hero{position:relative;display:grid;", storefront, StringComparison.Ordinal);
        Assert.Contains(".dp-market-hero-slide{grid-area:1/1;display:grid;", storefront, StringComparison.Ordinal);
        Assert.Contains("visibility:hidden;opacity:0;pointer-events:none", storefront, StringComparison.Ordinal);
        Assert.Contains(".dp-market-hero-slide.is-active{z-index:1;visibility:visible;opacity:1;pointer-events:auto}", storefront, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:760px)", storefront, StringComparison.Ordinal);
        Assert.Contains(".dp-market-hero-slide{grid-template-columns:1fr}", storefront, StringComparison.Ordinal);
        Assert.DoesNotContain(".dp-market-hero-slide{display:none", storefront, StringComparison.Ordinal);
    }

    private static string Source(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, "src", "BTCPayServer.Plugins.MakePay.DigitalProducts", .. segments]);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException($"Could not locate source file {Path.Combine(segments)}.");
    }
}
