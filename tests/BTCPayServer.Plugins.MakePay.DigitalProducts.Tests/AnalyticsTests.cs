#nullable enable
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class AnalyticsTests
{
    [Fact]
    public void Selected_provider_ids_are_normalized_and_validated_exclusively()
    {
        var settings = new DigitalDownloadsSettings
        {
            AnalyticsProvider = AnalyticsProvider.GoogleTagManager,
            GoogleTagManagerContainerId = "  gtm-abc123  ",
            GoogleAnalyticsMeasurementId = " g-unused123 "
        };

        DigitalAnalyticsBuilder.NormalizeConfiguration(settings);

        Assert.Equal("GTM-ABC123", settings.GoogleTagManagerContainerId);
        Assert.Equal("G-UNUSED123", settings.GoogleAnalyticsMeasurementId);
        Assert.Null(DigitalAnalyticsBuilder.ConfigurationError(settings));
        Assert.True(DigitalAnalyticsBuilder.HasConfiguredProvider(settings));
        Assert.Equal("google_tag_manager", DigitalAnalyticsBuilder.ProviderToken(settings));

        settings.AnalyticsProvider = AnalyticsProvider.GoogleAnalytics;
        Assert.Null(DigitalAnalyticsBuilder.ConfigurationError(settings));
        Assert.Equal("google_analytics", DigitalAnalyticsBuilder.ProviderToken(settings));

        settings.GoogleAnalyticsMeasurementId = "UA-LEGACY";
        Assert.NotNull(DigitalAnalyticsBuilder.ConfigurationError(settings));
        Assert.False(DigitalAnalyticsBuilder.HasConfiguredProvider(settings));
        Assert.Equal("disabled", DigitalAnalyticsBuilder.ProviderToken(settings));
    }

    [Fact]
    public void Analytics_id_annotations_accept_case_insensitive_valid_ids_and_reject_invalid_ids()
    {
        Assert.DoesNotContain(Validate(new DigitalDownloadsSettings
        {
            GoogleTagManagerContainerId = " gtm-a1b2c3 ",
            GoogleAnalyticsMeasurementId = " g-abc123def4 "
        }), result => result.MemberNames.Any(name =>
            name is nameof(DigitalDownloadsSettings.GoogleTagManagerContainerId) or
                nameof(DigitalDownloadsSettings.GoogleAnalyticsMeasurementId)));

        var invalid = Validate(new DigitalDownloadsSettings
        {
            GoogleTagManagerContainerId = "GTM_ABC",
            GoogleAnalyticsMeasurementId = "UA-123"
        });

        Assert.Contains(invalid, result => result.MemberNames.Contains(nameof(DigitalDownloadsSettings.GoogleTagManagerContainerId)));
        Assert.Contains(invalid, result => result.MemberNames.Contains(nameof(DigitalDownloadsSettings.GoogleAnalyticsMeasurementId)));
    }

    [Fact]
    public void Catalog_payload_uses_ga4_item_schema_and_stable_non_pii_values()
    {
        var product = Product("ebook", "Field Guide", 12.50m, DigitalProductType.PdfEbook, DigitalDeliveryMode.StreamAndDownload);

        var payload = DigitalAnalyticsBuilder.Catalog([product], " aed ", "ebooks", "Ebooks");
        var json = JsonSerializer.Serialize(payload);

        Assert.Equal("AED", payload.Currency);
        Assert.Equal(12.50m, payload.Value);
        Assert.Equal("ebooks", payload.ItemListId);
        Assert.Equal("Ebooks", payload.ItemListName);
        var item = Assert.Single(payload.Items);
        Assert.Equal("Download:ebook", item.ItemId);
        Assert.Equal("Field Guide", item.ItemName);
        Assert.Equal("PDF / ebook", item.ItemCategory);
        Assert.Equal("StreamAndDownload", item.ItemVariant);
        Assert.Equal(0, item.Index);
        Assert.Contains("\"item_id\":\"Download:ebook\"", json);
        Assert.Contains("\"item_category\":\"PDF / ebook\"", json);
    }

    [Fact]
    public void Purchase_payload_contains_transaction_and_totals_but_never_checkout_secrets()
    {
        var checkout = new DigitalCheckout
        {
            Id = "checkout-123",
            StoreId = "store-1",
            BuyerEmail = "buyer-secret@example.com",
            Currency = "usd",
            PublicAccessTokenHash = "secret-token-hash",
            ProtectedPublicAccessToken = "protected-secret-token",
            Lines =
            [
                new DigitalCheckoutLine
                {
                    Kind = DigitalProductKind.Download,
                    ProductId = "audio-pack",
                    Name = "Audio pack",
                    Quantity = 2,
                    UnitPrice = 9.25m,
                    DigitalProductSnapshot = new DigitalProductSnapshot
                    {
                        ProductType = DigitalProductType.Audio,
                        DeliveryMode = DigitalDeliveryMode.StreamAndDownload
                    }
                }
            ]
        };

        var payload = DigitalAnalyticsBuilder.Checkout(checkout, purchase: true);
        var json = JsonSerializer.Serialize(payload);

        Assert.StartsWith("mpd_", payload.TransactionId);
        Assert.Equal(68, payload.TransactionId!.Length);
        Assert.DoesNotContain(checkout.Id, payload.TransactionId, StringComparison.Ordinal);
        Assert.Equal("BTCPay Server", payload.PaymentType);
        Assert.Equal(18.50m, payload.Value);
        Assert.Equal(2, Assert.Single(payload.Items).Quantity);
        Assert.DoesNotContain(checkout.BuyerEmail, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(checkout.PublicAccessTokenHash, json, StringComparison.Ordinal);
        Assert.DoesNotContain(checkout.ProtectedPublicAccessToken, json, StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_runtime_enforces_privacy_dedupe_and_ecommerce_reset_contract()
    {
        var assembly = typeof(DigitalAnalyticsBuilder).Assembly;
        var resourceName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith("makepay-analytics.js", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd();

        Assert.Contains("window.dataLayer = window.dataLayer || []", script);
        Assert.Contains("if (!mayTrack()) return false", script);
        Assert.Contains("const reset = { ecommerce: null }", script);
        Assert.Contains("purchaseOnce", script);
        Assert.Contains("navigator.doNotTrack", script);
        Assert.Contains("updateGoogleConsent('default'", script);
        Assert.Contains("updateGoogleConsent('update'", script);
        Assert.Contains("const reloadWithoutProvider = !granted && providerStarted", script);
        Assert.Contains("if (reloadWithoutProvider) window.location.reload()", script);
        Assert.Contains("analytics_storage: granted ? 'granted' : 'denied'", script);
        Assert.Contains("ad_storage: 'denied'", script);
        Assert.Contains("allow_google_signals: false", script);
        Assert.Contains("allow_ad_personalization_signals: false", script);
        Assert.Contains("const safePageLocation = `${window.location.origin}${safePagePath}`", script);
        Assert.Contains("sanitizePagePath(window.location.pathname)", script);
        Assert.Contains(":checkout_id", script);
        Assert.Contains(":order_id", script);
        Assert.Contains("if (dnt) return false;\n        if (!configured) return true;", script);
        Assert.Contains("page_referrer: safePageReferrer", script);
        Assert.DoesNotContain("window.location.href", script);
        Assert.DoesNotContain("window.location.search", script);
        Assert.DoesNotContain("accessToken", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PaymentAndPurchaseViewsUseHashedAnalyticsDedupeIdentifiers()
    {
        var views = new[] { "Payment.cshtml", "Purchase.cshtml" };
        foreach (var view in views)
        {
            var source = File.ReadAllText(RepositoryFile(
                "src",
                "BTCPayServer.Plugins.MakePay.DigitalProducts",
                "Views",
                "DigitalDownloads",
                "Public",
                view));
            Assert.Contains("DigitalAnalyticsBuilder.AnalyticsTransactionId(Model.Checkout)", source, StringComparison.Ordinal);
            Assert.Contains("analyticsDedupeId", source, StringComparison.Ordinal);
        }
    }

    private static List<ValidationResult> Validate(DigitalDownloadsSettings settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return results;
    }

    private static string RepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Could not locate repository file {Path.Combine(segments)}.");
    }

    private static StoreProductViewModel Product(
        string id,
        string name,
        decimal price,
        DigitalProductType productType,
        DigitalDeliveryMode deliveryMode) => new()
    {
        Kind = DigitalProductKind.Download,
        Id = id,
        Slug = id,
        Name = name,
        Description = "Description",
        Price = price,
        ProductType = productType,
        DeliveryMode = deliveryMode
    };
}
