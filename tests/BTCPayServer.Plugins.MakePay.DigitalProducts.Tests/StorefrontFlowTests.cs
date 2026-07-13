using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class StorefrontFlowTests
{
    private static IDataProtectionProvider Protection() => new EphemeralDataProtectionProvider();

    [Fact]
    public void Login_code_is_encrypted_one_time_and_attempt_limited()
    {
        var service = new CustomerAccessService(Protection());
        var (challenge, code) = service.CreateChallenge(" Buyer@Example.com ", 10);
        Assert.Equal("buyer@example.com", challenge.NormalizedEmail);
        Assert.DoesNotContain(code, challenge.ProtectedCode);
        Assert.True(service.Verify(challenge, code));
        Assert.False(service.Verify(challenge, "000000" == code ? "111111" : "000000"));
        challenge.ConsumedAt = DateTimeOffset.UtcNow;
        Assert.False(service.Verify(challenge, code));
        challenge.ConsumedAt = null;
        challenge.Attempts = 6;
        Assert.False(service.Verify(challenge, code));
    }

    [Fact]
    public void Customer_session_and_checkout_token_are_store_scoped()
    {
        var service = new CustomerAccessService(Protection());
        var session = service.CreateSession("store-a", "Buyer@Example.com", 2);
        Assert.Equal("buyer@example.com", service.ReadSession(session, "store-a"));
        Assert.Null(service.ReadSession(session, "store-b"));
        Assert.Null(service.ReadSession(session + "tampered", "store-a"));
        var checkout = new DigitalCheckout { StoreId = "store-a", BuyerEmail = "buyer@example.com" };
        var token = service.CreateCheckoutAccess(checkout);
        Assert.True(service.CanAccess(checkout, token));
        Assert.True(service.CanAccess(checkout, null, "BUYER@example.com"));
        Assert.False(service.CanAccess(checkout, "wrong"));
        Assert.Equal(token, service.RecoverCheckoutAccess(checkout));
    }

    [Fact]
    public void Cart_cookie_is_encrypted_scoped_and_normalized()
    {
        var service = new DigitalCartService(Protection());
        var cart = new DigitalCartState();
        service.Add(cart, DigitalProductKind.Download, "guide", 8);
        service.Add(cart, DigitalProductKind.Download, "GUIDE", 1);
        service.Add(cart, DigitalProductKind.License, "pro", 7);
        service.Add(cart, DigitalProductKind.License, "pro", 9);
        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(1, cart.Lines.Single(line => line.Kind == DigitalProductKind.Download).Quantity);
        Assert.Equal(10, cart.Lines.Single(line => line.Kind == DigitalProductKind.License).Quantity);
        var protectedCart = service.Protect("store-a", cart);
        Assert.DoesNotContain("guide", protectedCart, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, service.Read("store-a", protectedCart).Lines.Count);
        Assert.Empty(service.Read("store-b", protectedCart).Lines);
        Assert.Empty(service.Read("store-a", protectedCart + "tampered").Lines);
    }

    [Fact]
    public void Mixed_catalog_checkout_snapshots_current_prices_and_quantities()
    {
        var service = new DigitalCheckoutService();
        var catalog = service.BuildCatalog(
            [new DigitalProduct { Id = "download", Name = "Guide", Price = 9.99m, Active = true }, new DigitalProduct { Id = "hidden", Name = "Hidden", Price = 1m, Active = false }],
            [new LicenseProduct { Id = "license", Name = "Pro", Price = 49m, Active = true, MaxActivations = 3 }]);
        var cart = new DigitalCartState { Lines = [new() { Kind = DigitalProductKind.Download, ProductId = "download", Quantity = 9 }, new() { Kind = DigitalProductKind.License, ProductId = "license", Quantity = 2 }] };
        var lines = service.ResolveCart(cart, catalog);
        var checkout = service.Create("store", " BUYER@Example.com ", "usd", lines, "https://example.com");
        Assert.Equal(2, catalog.Count);
        Assert.Equal(3, checkout.Lines.Sum(line => line.Quantity));
        Assert.Equal(107.99m, checkout.Total);
        Assert.Equal("USD", checkout.Currency);
        Assert.Equal("buyer@example.com", checkout.BuyerEmail);
        Assert.Equal(["new-license", "new-product"], catalog.Select(product => product.Slug).OrderBy(slug => slug));
    }

    [Fact]
    public void Storefront_categories_hide_empty_and_disabled_entries()
    {
        var settings = new DigitalDownloadsSettings
        {
            StorefrontCategories =
            [
                new() { Id = "downloads", Name = "Downloads", Slug = "downloads", Rule = DigitalStoreCategoryRule.Downloads },
                new() { Id = "licenses", Name = "Licenses", Slug = "licenses", Rule = DigitalStoreCategoryRule.Licenses },
                new() { Id = "empty", Name = "Empty", Slug = "empty", Rule = DigitalStoreCategoryRule.Custom },
                new() { Id = "hidden", Name = "Hidden", Slug = "hidden", Rule = DigitalStoreCategoryRule.Downloads, Visible = false }
            ]
        };

        var categories = DigitalStorefrontBuilder.BuildCategories(settings, [Product(DigitalProductKind.Download, "guide")]);

        var category = Assert.Single(categories);
        Assert.Equal("downloads", category.Slug);
    }

    [Fact]
    public void Custom_category_filters_only_selected_products()
    {
        var guide = Product(DigitalProductKind.Download, "guide");
        var pro = Product(DigitalProductKind.License, "pro");
        var settings = new DigitalDownloadsSettings
        {
            StorefrontCategories =
            [
                new()
                {
                    Id = "featured",
                    Name = "Featured",
                    Slug = "featured",
                    ProductReferences = [DigitalStorefrontBuilder.ProductReference(pro)]
                }
            ]
        };

        var category = Assert.Single(DigitalStorefrontBuilder.BuildCategories(settings, [guide, pro]));
        var product = Assert.Single(DigitalStorefrontBuilder.FilterCatalog([guide, pro], category));
        Assert.Equal("pro", product.Id);
    }

    [Fact]
    public void Hero_product_attachment_uses_catalog_anchor_and_safe_fallbacks()
    {
        var product = Product(DigitalProductKind.License, "pro");
        var settings = new DigitalDownloadsSettings
        {
            HeroSlides =
            [
                new()
                {
                    Id = "hero",
                    Headline = "Pro",
                    ImageUrl = "javascript:alert(1)",
                    LinkUrl = "javascript:alert(1)",
                    ProductReference = DigitalStorefrontBuilder.ProductReference(product)
                }
            ]
        };

        var slide = Assert.Single(DigitalStorefrontBuilder.BuildHeroSlides(settings, [product], "/safe-hero.png"));
        Assert.Equal("/safe-hero.png", slide.ImageUrl);
        Assert.Equal("#product-license-pro", slide.LinkUrl);
        Assert.False(DigitalStorefrontBuilder.IsSafePublicResourceUrl("//evil.example/image.png"));
        Assert.True(DigitalStorefrontBuilder.IsSafePublicResourceUrl("/stores/demo/image.png"));
    }

    [Fact]
    public void Hero_product_attachment_can_link_to_product_detail_page()
    {
        var product = Product(DigitalProductKind.License, "pro");
        var settings = new DigitalDownloadsSettings
        {
            HeroSlides =
            [
                new()
                {
                    Id = "hero",
                    Headline = "Pro",
                    ProductReference = DigitalStorefrontBuilder.ProductReference(product)
                }
            ]
        };

        var slide = Assert.Single(DigitalStorefrontBuilder.BuildHeroSlides(
            settings,
            [product],
            "/safe-hero.png",
            linkedProduct => $"/products/{DigitalStorefrontBuilder.ProductKindSegment(linkedProduct)}/{linkedProduct.Slug}"));

        Assert.Equal("/products/license/pro", slide.LinkUrl);
    }

    [Fact]
    public void MakePay_attribution_is_enforced_and_cannot_be_overridden()
    {
        var settings = new DigitalDownloadsSettings
        {
            ShowMakePayPromotion = false,
            PromotionText = "Custom attribution"
        };

        DigitalStorefrontBuilder.EnforceMakePayAttribution(settings);

        Assert.True(settings.ShowMakePayPromotion);
        Assert.Equal(DigitalStorefrontBuilder.EnforcedPromotionText, settings.PromotionText);
    }

    private static StoreProductViewModel Product(DigitalProductKind kind, string id) => new()
    {
        Kind = kind,
        Id = id,
        Slug = id,
        Name = id,
        Description = "Description",
        Price = 1m
    };
}
