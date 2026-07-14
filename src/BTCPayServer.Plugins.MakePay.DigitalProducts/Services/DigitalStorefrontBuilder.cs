#nullable enable
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

public static class DigitalStorefrontBuilder
{
    public const string EnforcedPromotionText = "Created by MakePay.io — accept 90+ currencies in a decentralized way with BTCPay Server.";

    public static void EnforceMakePayAttribution(DigitalDownloadsSettings settings)
    {
        settings.ShowMakePayPromotion = true;
        settings.PromotionText = EnforcedPromotionText;
    }

    public static IReadOnlyList<DigitalStoreCategoryViewModel> BuildCategories(
        DigitalDownloadsSettings settings,
        IReadOnlyList<StoreProductViewModel> catalog)
    {
        var result = new List<DigitalStoreCategoryViewModel>();
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in settings.EffectiveStorefrontCategories().Where(category => category.Visible))
        {
            if (string.IsNullOrWhiteSpace(category.Name) || string.IsNullOrWhiteSpace(category.Slug) || !slugs.Add(category.Slug)) continue;
            var references = catalog
                .Where(product => Matches(category, product))
                .Select(ProductReference)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (references.Count == 0) continue;
            result.Add(new DigitalStoreCategoryViewModel
            {
                Slug = category.Slug,
                Name = category.Name,
                ProductReferences = references
            });
        }

        return result;
    }

    public static IReadOnlyList<StoreProductViewModel> FilterCatalog(
        IReadOnlyList<StoreProductViewModel> catalog,
        DigitalStoreCategoryViewModel? category) =>
        category is null
            ? catalog
            : catalog.Where(product => category.ProductReferences.Contains(ProductReference(product))).ToList();

    public static IReadOnlyList<DigitalHeroSlideViewModel> BuildHeroSlides(
        DigitalDownloadsSettings settings,
        IReadOnlyList<StoreProductViewModel> catalog,
        string fallbackImageUrl,
        Func<StoreProductViewModel, string>? productLink = null)
    {
        var products = catalog.ToDictionary(ProductReference, StringComparer.OrdinalIgnoreCase);
        return settings.EffectiveHeroSlides()
            .Where(slide => slide.Visible && !string.IsNullOrWhiteSpace(slide.Headline))
            .Take(12)
            .Select(slide =>
            {
                StoreProductViewModel? product = null;
                var hasProduct = !string.IsNullOrWhiteSpace(slide.ProductReference) && products.TryGetValue(slide.ProductReference, out product);
                var link = hasProduct
                    ? productLink?.Invoke(product!) ?? $"#{ProductAnchor(product!)}"
                    : IsSafePublicLink(slide.LinkUrl) ? slide.LinkUrl : "#products";
                return new DigitalHeroSlideViewModel
                {
                    Id = slide.Id,
                    Eyebrow = slide.Eyebrow,
                    Headline = slide.Headline,
                    SupportingCopy = slide.SupportingCopy,
                    ImageUrl = !string.IsNullOrWhiteSpace(slide.ImageUrl) && IsSafePublicResourceUrl(slide.ImageUrl) ? slide.ImageUrl : fallbackImageUrl,
                    ButtonText = string.IsNullOrWhiteSpace(slide.ButtonText) ? (hasProduct ? $"Explore {product!.Name}" : "Explore products") : slide.ButtonText,
                    LinkUrl = link
                };
            }).ToList();
    }

    public static string ProductReference(StoreProductViewModel product) => $"{product.Kind}:{product.Id}";

    public static string ProductKindSegment(StoreProductViewModel product) =>
        product.Kind == DigitalProductKind.License ? "license" : "download";

    public static string ProductAssetKind(StoreProductViewModel product) =>
        product.Kind == DigitalProductKind.License ? "license" : product.ProductType switch
        {
            DigitalProductType.PdfEbook => "ebook",
            DigitalProductType.Audio => "audio",
            DigitalProductType.Video => "video",
            DigitalProductType.PhotosArt => "photo",
            _ => "download"
        };

    public static string ProductTypeLabel(StoreProductViewModel product) =>
        product.Kind == DigitalProductKind.License ? "Software license" : ProductTypeLabel(product.ProductType ?? DigitalProductType.FileDownload);

    public static string ProductTypeLabel(DigitalProductType type) => type switch
    {
        DigitalProductType.PdfEbook => "PDF / ebook",
        DigitalProductType.Audio => "Music & audio",
        DigitalProductType.Video => "Video content",
        DigitalProductType.PhotosArt => "Photos & art",
        _ => "File download"
    };

    public static string DeliveryLabel(DigitalProduct product) => product.DeliveryMode switch
    {
        DigitalDeliveryMode.Stream => product.ProductType == DigitalProductType.PdfEbook ? "Read online" : "Protected streaming",
        DigitalDeliveryMode.StreamAndDownload => product.ProductType == DigitalProductType.PdfEbook ? "Read online or download" : "Stream or download",
        _ => "Protected private download"
    };

    public static string PreviewActionLabel(DigitalProductType type) => type switch
    {
        DigitalProductType.PdfEbook => "Read preview",
        DigitalProductType.Audio => "Listen to demo",
        DigitalProductType.Video => "Watch trailer",
        DigitalProductType.PhotosArt => "View gallery",
        _ => "Preview sample"
    };

    public static string ProductMeta(DigitalProduct product)
    {
        var detail = product.ProductType switch
        {
            DigitalProductType.PdfEbook when product.PageCount is { } pages => $"{pages} pages",
            DigitalProductType.Audio when product.DurationSeconds is { } seconds => FormatDuration(seconds),
            DigitalProductType.Video when product.DurationSeconds is { } seconds => FormatDuration(seconds),
            DigitalProductType.PhotosArt when product.AssetCount is { } count => $"{count} asset{(count == 1 ? "" : "s")}",
            _ when product.FileSize is { } bytes => FormatBytes(bytes),
            _ => ProductTypeLabel(product.ProductType)
        };
        return $"{detail} · {DeliveryLabel(product)}";
    }

    public static string FormatDuration(int seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, (double)bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    public static string ProductAnchor(StoreProductViewModel product)
    {
        var id = string.Concat(product.Id.Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
        return $"product-{product.Kind.ToString().ToLowerInvariant()}-{id}";
    }

    public static bool IsSafePublicResourceUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsAbsoluteHttpUrl(value) || IsSafeRootedPath(value);

    public static string? NormalizePublicResourceUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? SafePublicResourceUrl(string? value)
    {
        var normalized = NormalizePublicResourceUrl(value);
        return normalized is not null && IsSafePublicResourceUrl(normalized) ? normalized : null;
    }

    public static bool IsSafePublicLink(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.StartsWith('#') || IsSafeRootedPath(value) || IsAbsoluteHttpUrl(value));

    private static bool Matches(DigitalStoreCategory category, StoreProductViewModel product) => category.Rule switch
    {
        DigitalStoreCategoryRule.Downloads => product.Kind == DigitalProductKind.Download && product.ProductType is null or DigitalProductType.FileDownload,
        DigitalStoreCategoryRule.Licenses => product.Kind == DigitalProductKind.License,
        DigitalStoreCategoryRule.PdfEbooks => product.Kind == DigitalProductKind.Download && product.ProductType == DigitalProductType.PdfEbook,
        DigitalStoreCategoryRule.Audio => product.Kind == DigitalProductKind.Download && product.ProductType == DigitalProductType.Audio,
        DigitalStoreCategoryRule.Video => product.Kind == DigitalProductKind.Download && product.ProductType == DigitalProductType.Video,
        DigitalStoreCategoryRule.PhotosArt => product.Kind == DigitalProductKind.Download && product.ProductType == DigitalProductType.PhotosArt,
        _ => category.ProductReferences.Contains(ProductReference(product), StringComparer.OrdinalIgnoreCase)
    };

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static bool IsSafeRootedPath(string value) =>
        value.StartsWith('/') &&
        !value.StartsWith("//", StringComparison.Ordinal) &&
        !value.Contains('\\');
}
