#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

public static partial class DigitalAnalyticsBuilder
{
    public const string PluginContext = "digital_products";
    public const string SchemaVersion = "1";

    [GeneratedRegex("^GTM-[A-Z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex GoogleTagManagerIdPattern();

    [GeneratedRegex("^G-[A-Z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex GoogleAnalyticsIdPattern();

    public static void NormalizeConfiguration(DigitalDownloadsSettings settings)
    {
        settings.GoogleTagManagerContainerId = NormalizeId(settings.GoogleTagManagerContainerId);
        settings.GoogleAnalyticsMeasurementId = NormalizeId(settings.GoogleAnalyticsMeasurementId);
    }

    public static string? ConfigurationError(DigitalDownloadsSettings settings) => settings.AnalyticsProvider switch
    {
        AnalyticsProvider.GoogleTagManager when !IsGoogleTagManagerId(settings.GoogleTagManagerContainerId) =>
            "Enter a valid Google Tag Manager container ID before enabling Google Tag Manager.",
        AnalyticsProvider.GoogleAnalytics when !IsGoogleAnalyticsId(settings.GoogleAnalyticsMeasurementId) =>
            "Enter a valid Google Analytics 4 measurement ID before enabling Google Analytics.",
        _ => null
    };

    public static bool HasConfiguredProvider(DigitalDownloadsSettings settings) => settings.AnalyticsProvider switch
    {
        AnalyticsProvider.GoogleTagManager => IsGoogleTagManagerId(settings.GoogleTagManagerContainerId),
        AnalyticsProvider.GoogleAnalytics => IsGoogleAnalyticsId(settings.GoogleAnalyticsMeasurementId),
        _ => false
    };

    public static string ProviderToken(DigitalDownloadsSettings settings) => settings.AnalyticsProvider switch
    {
        AnalyticsProvider.GoogleTagManager when IsGoogleTagManagerId(settings.GoogleTagManagerContainerId) => "google_tag_manager",
        AnalyticsProvider.GoogleAnalytics when IsGoogleAnalyticsId(settings.GoogleAnalyticsMeasurementId) => "google_analytics",
        _ => "disabled"
    };

    public static bool IsGoogleTagManagerId(string? value) =>
        value is not null && GoogleTagManagerIdPattern().IsMatch(value);

    public static bool IsGoogleAnalyticsId(string? value) =>
        value is not null && GoogleAnalyticsIdPattern().IsMatch(value);

    public static DigitalAnalyticsItem Item(StoreProductViewModel product, int quantity = 1, int? index = null) => new()
    {
        ItemId = DigitalStorefrontBuilder.ProductReference(product),
        ItemName = product.Name,
        ItemCategory = DigitalStorefrontBuilder.ProductTypeLabel(product),
        ItemVariant = product.Kind == DigitalProductKind.License
            ? "License"
            : product.DeliveryMode?.ToString() ?? DigitalDeliveryMode.Download.ToString(),
        Price = product.Price,
        Quantity = Math.Max(1, quantity),
        Index = index
    };

    public static DigitalAnalyticsItem Item(DigitalCheckoutLine line, int? index = null) => new()
    {
        ItemId = $"{line.Kind}:{line.ProductId}",
        ItemName = line.Name,
        ItemCategory = line.Kind == DigitalProductKind.License
            ? "Software license"
            : DigitalStorefrontBuilder.ProductTypeLabel(line.DigitalProductSnapshot?.ProductType ?? DigitalProductType.FileDownload),
        ItemVariant = line.Kind == DigitalProductKind.License
            ? "License"
            : line.DigitalProductSnapshot?.DeliveryMode.ToString() ?? DigitalDeliveryMode.Download.ToString(),
        Price = line.UnitPrice,
        Quantity = Math.Max(1, line.Quantity),
        Index = index
    };

    public static DigitalAnalyticsPayload Catalog(
        IReadOnlyList<StoreProductViewModel> products,
        string currency,
        string listId,
        string listName) => Payload(
        products.Select((product, index) => Item(product, index: index)).ToList(),
        currency,
        itemListId: listId,
        itemListName: listName);

    public static DigitalAnalyticsPayload Product(
        StoreProductViewModel product,
        string currency,
        int quantity = 1,
        string? listId = null,
        string? listName = null) => Payload(
        [Item(product, quantity)],
        currency,
        itemListId: listId,
        itemListName: listName);

    public static DigitalAnalyticsPayload Cart(IReadOnlyList<CartLineViewModel> lines, string currency) => Payload(
        lines.Select((line, index) => Item(line.Product, line.Quantity, index)).ToList(),
        currency);

    public static DigitalAnalyticsPayload Checkout(
        DigitalCheckout checkout,
        bool paymentInformation = false,
        bool purchase = false) => Payload(
        checkout.Lines.Select((line, index) => Item(line, index)).ToList(),
        checkout.Currency,
        transactionId: purchase ? AnalyticsTransactionId(checkout) : null,
        paymentType: paymentInformation || purchase ? "BTCPay Server" : null);

    public static string AnalyticsTransactionId(DigitalCheckout checkout)
    {
        var source = Encoding.UTF8.GetBytes($"{PluginContext}\n{checkout.StoreId}\n{checkout.Id}");
        return $"mpd_{Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant()}";
    }

    private static DigitalAnalyticsPayload Payload(
        IReadOnlyList<DigitalAnalyticsItem> items,
        string currency,
        string? transactionId = null,
        string? itemListId = null,
        string? itemListName = null,
        string? paymentType = null) => new()
    {
        Currency = NormalizeCurrency(currency),
        Value = items.Sum(item => item.Price * item.Quantity),
        Items = items,
        TransactionId = transactionId,
        ItemListId = itemListId,
        ItemListName = itemListName,
        PaymentType = paymentType
    };

    private static string? NormalizeId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string NormalizeCurrency(string currency) =>
        string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
}
