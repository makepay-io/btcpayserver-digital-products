#nullable enable
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using BTCPayServer.Plugins.MakePay.LicenseManager.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
[AutoValidateAntiforgeryToken]
[Route("plugins/{storeId}/digital-downloads")]
public sealed class DigitalDownloadsAdminController(
    StoreRepository stores,
    DigitalDownloadsRepository repository,
    LicenseRepository licenses,
    DigitalCheckoutService checkoutService,
    ProductFileService files,
    DownloadTokenService secrets,
    DigitalPublicUrlService publicUrls,
    DigitalProductsAppService digitalApps) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string storeId,
        string? section = null,
        string? orderSearch = null,
        string? orderProductId = null,
        string? orderStatus = null,
        int orderPage = 1,
        int orderPageSize = 25)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var activeSection = string.Equals(section, "licenses", StringComparison.OrdinalIgnoreCase) ? "licenses" : "products";
        var products = await repository.GetProducts(storeId);
        var orderPageModel = DigitalOrderAdminQuery.Apply(
            await repository.GetOrders(storeId),
            products,
            orderSearch,
            orderProductId,
            orderStatus,
            orderPage,
            orderPageSize);
        ViewData.SetActivePage("DigitalDownloads", "Digital Products", activeSection == "licenses" ? "License keys" : "Products");
        return View("~/Views/DigitalDownloads/Index.cshtml", new DigitalDownloadsDashboardViewModel
        {
            StoreId = storeId,
            Settings = await repository.GetSettings(storeId),
            Products = products,
            OrderPage = orderPageModel,
            LicenseSettings = await licenses.GetSettings(storeId),
            LicenseProducts = await licenses.GetProducts(storeId),
            Licenses = (await licenses.GetLicenses(storeId)).Take(200).ToList(),
            LicenseOrders = (await licenses.GetOrders(storeId)).Take(100).ToList(),
            ActiveSection = activeSection
        });
    }

    [HttpGet("products/new")]
    [HttpGet("products/{productId}")]
    public async Task<IActionResult> Product(string storeId, string? productId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var product = string.IsNullOrWhiteSpace(productId) ? new DigitalProduct() : await repository.GetProduct(storeId, productId);
        if (product is null) return NotFound();
        ViewData["StoreId"] = storeId;
        ViewData.SetActivePage("DigitalDownloads", "Digital Products", product.Id.Length == 0 ? "New product" : product.Name);
        return View("~/Views/DigitalDownloads/Product.cshtml", product);
    }

    [HttpPost("products/{productId}")]
    public async Task<IActionResult> SaveProduct(
        string storeId,
        string productId,
        DigitalProduct posted,
        IFormFile? upload,
        IFormFile? coverUpload,
        List<IFormFile>? previewUploads,
        string? removePreviewAssetIds,
        CancellationToken cancellationToken)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var existing = productId == "new" ? null : await repository.GetProduct(storeId, productId);
        posted.Id = existing?.Id ?? Guid.NewGuid().ToString("N");
        posted.CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;
        posted.PreviewAssets = existing?.PreviewAssets.ToList() ?? [];
        var removedIds = (removePreviewAssetIds ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        posted.PreviewAssets.RemoveAll(asset => removedIds.Contains(asset.Id));

        NormalizeProduct(posted);
        if (upload is not null)
        {
            if (await files.ValidateProductAsset(posted.ProductType, upload, cancellationToken) is { } uploadError)
            {
                ModelState.AddModelError(nameof(upload), uploadError);
            }
            else
            {
                var saved = await files.SaveLocal(storeId, posted.Id, upload, cancellationToken, posted.ProductType);
                posted.StorageKind = ProductStorageKind.Local;
                posted.StorageLocation = saved.RelativePath;
                posted.DownloadFileName = saved.FileName;
                posted.ContentType = saved.ContentType;
                posted.FileSize = saved.Size;
            }
        }
        else if (posted.StorageKind == ProductStorageKind.Local && existing?.StorageKind == ProductStorageKind.Local)
        {
            posted.StorageLocation = existing.StorageLocation;
            posted.DownloadFileName = existing.DownloadFileName;
            posted.ContentType = existing.ContentType;
            posted.FileSize = existing.FileSize;
        }
        else if (posted.StorageKind == ProductStorageKind.Local)
        {
            ModelState.AddModelError(nameof(upload), "Upload the protected product file before saving a local product.");
        }

        if (coverUpload is not null && ProductFileService.ValidateStorefrontAsset(coverUpload) is { } coverError)
            ModelState.AddModelError(nameof(coverUpload), coverError);

        previewUploads ??= [];
        if (posted.ProductType != DigitalProductType.PhotosArt && posted.PreviewAssets.Count + previewUploads.Count > 1)
            ModelState.AddModelError(nameof(previewUploads), "PDF, audio, video, and file products support one public preview file.");
        if (posted.PreviewAssets.Count + previewUploads.Count > 12)
            ModelState.AddModelError(nameof(previewUploads), "A product can have up to 12 public preview assets.");
        foreach (var preview in previewUploads)
        {
            if (await files.ValidatePreviewAsset(posted.ProductType, preview, cancellationToken) is { } previewError)
                ModelState.AddModelError(nameof(previewUploads), previewError);
        }

        ValidateProductConfiguration(posted);
        if (!ModelState.IsValid)
        {
            ViewData["StoreId"] = storeId;
            return View("~/Views/DigitalDownloads/Product.cshtml", posted);
        }

        if (coverUpload is not null)
        {
            var saved = await files.SaveStorefrontAsset(storeId, $"product-{posted.Id}", coverUpload, cancellationToken);
            posted.ImageUrl = Url.Action(nameof(DigitalDownloadsPublicController.StorefrontAsset), "DigitalDownloadsPublic", new { storeId, assetId = $"product-{posted.Id}", fileName = saved.FileName });
        }

        var nextSortOrder = posted.PreviewAssets.Count == 0 ? 0 : posted.PreviewAssets.Max(asset => asset.SortOrder) + 1;
        foreach (var preview in previewUploads)
        {
            var saved = await files.SavePreviewLocal(storeId, posted.Id, PreviewLabel(posted.ProductType, posted.PreviewAssets.Count), preview, cancellationToken, posted.ProductType);
            saved.SortOrder = nextSortOrder++;
            saved.AltText = posted.ProductType == DigitalProductType.PhotosArt ? $"Preview of {posted.Name}" : null;
            saved.Watermarked = posted.ProductType == DigitalProductType.PhotosArt && posted.WatermarkPreviews &&
                                saved.ContentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase) &&
                                saved.FileName.Contains("watermarked", StringComparison.OrdinalIgnoreCase);
            posted.PreviewAssets.Add(saved);
        }
        posted.PreviewEnabled = posted.PreviewEnabled && posted.PreviewAssets.Count > 0;
        try { await repository.SaveProduct(storeId, posted); }
        catch (InvalidOperationException ex) { ModelState.AddModelError(nameof(posted.Slug), ex.Message); ViewData["StoreId"] = storeId; return View("~/Views/DigitalDownloads/Product.cshtml", posted); }
        TempData.SetStatusMessageModel(new StatusMessageModel { Severity = StatusMessageModel.StatusSeverity.Success, Message = "Product saved." });
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpPost("products/{productId}/delete")]
    public async Task<IActionResult> DeleteProduct(string storeId, string productId)
    {
        var legacyOrders = (await repository.GetOrders(storeId)).Any(order => order.ProductId == productId && order.ProductSnapshot is null);
        if (legacyOrders)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel { Severity = StatusMessageModel.StatusSeverity.Error, Message = "This product has earlier purchases without a content snapshot. Unpublish it instead so those customers keep access." });
            return RedirectToAction(nameof(Product), new { storeId, productId });
        }
        await repository.DeleteProduct(storeId, productId);
        TempData.SetStatusMessageModel(new StatusMessageModel { Severity = StatusMessageModel.StatusSeverity.Success, Message = "Product removed from the catalog." });
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var settings = await repository.GetSettings(storeId);
        await PrepareSettingsViewData(storeId, settings: settings);
        return View("~/Views/DigitalDownloads/Settings.cshtml", settings);
    }

    [HttpPost("settings")]
    public async Task<IActionResult> SaveSettings(
        string storeId,
        DigitalDownloadsSettings posted,
        string? heroSlidesJson,
        string? categoriesJson,
        IFormFile? logoUpload,
        IFormFile? faviconUpload,
        string? s3SecretKey,
        string? remoteAuthorizationValue,
        CancellationToken cancellationToken)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var existing = await repository.GetSettings(storeId);
        var catalog = checkoutService.BuildCatalog(await repository.GetProducts(storeId), await licenses.GetProducts(storeId));
        var validProductReferences = catalog.Select(DigitalStorefrontBuilder.ProductReference).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var slides = DeserializeEditorState<DigitalHeroSlide>(heroSlidesJson, nameof(heroSlidesJson), 12);
        var categories = DeserializeEditorState<DigitalStoreCategory>(categoriesJson, nameof(categoriesJson), 24);
        if (slides is not null) posted.HeroSlides = slides;
        if (categories is not null) posted.StorefrontCategories = categories;

        posted.FaviconUrl = DigitalStorefrontBuilder.NormalizePublicResourceUrl(posted.FaviconUrl);
        ValidateEditorState(posted, validProductReferences);
        DigitalAnalyticsBuilder.NormalizeConfiguration(posted);
        // Retain inactive provider IDs for convenient switching, but do not let an
        // unused value prevent saving the selected provider or dataLayer-only mode.
        if (posted.AnalyticsProvider != AnalyticsProvider.GoogleTagManager)
            ModelState.Remove(nameof(posted.GoogleTagManagerContainerId));
        if (posted.AnalyticsProvider != AnalyticsProvider.GoogleAnalytics)
            ModelState.Remove(nameof(posted.GoogleAnalyticsMeasurementId));
        if (DigitalAnalyticsBuilder.ConfigurationError(posted) is { } analyticsError)
            ModelState.AddModelError(nameof(posted.AnalyticsProvider), analyticsError);
        posted.ProtectedS3SecretKey = string.IsNullOrWhiteSpace(s3SecretKey) ? existing.ProtectedS3SecretKey : secrets.ProtectSecret(s3SecretKey);
        posted.ProtectedRemoteAuthorizationValue = string.IsNullOrWhiteSpace(remoteAuthorizationValue) ? existing.ProtectedRemoteAuthorizationValue : secrets.ProtectSecret(remoteAuthorizationValue);
        posted.CustomerAccountsEnabled = true;
        if (posted.DefaultDownloadLimit < 1 || posted.DefaultLinkHours < 1) ModelState.AddModelError("", "Download limit and link lifetime must be at least one.");
        if (!string.IsNullOrWhiteSpace(posted.RemoteAuthorizationHeader) && !ProductFileService.IsSafeHeaderName(posted.RemoteAuthorizationHeader)) ModelState.AddModelError(nameof(posted.RemoteAuthorizationHeader), "Use a valid HTTP header name; connection, cookie, and framing headers are not allowed.");
        if (!string.IsNullOrWhiteSpace(remoteAuthorizationValue) && (remoteAuthorizationValue.Contains('\r') || remoteAuthorizationValue.Contains('\n'))) ModelState.AddModelError("remoteAuthorizationValue", "Authorization values cannot contain line breaks.");
        if (!ModelState.IsValid)
        {
            await PrepareSettingsViewData(storeId, catalog, posted);
            return View("~/Views/DigitalDownloads/Settings.cshtml", posted);
        }

        if (logoUpload is not null && ProductFileService.ValidateStorefrontAsset(logoUpload) is { } logoError)
            ModelState.AddModelError(nameof(logoUpload), logoError);
        if (faviconUpload is not null && ProductFileService.ValidateFaviconAsset(faviconUpload) is { } faviconError)
            ModelState.AddModelError(nameof(faviconUpload), faviconError);
        foreach (var slide in posted.HeroSlides)
        {
            var upload = NonEmptyUpload(Request.Form.Files, $"heroSlideUpload_{slide.Id}");
            if (upload is not null && ProductFileService.ValidateStorefrontAsset(upload) is { } slideError)
                ModelState.AddModelError($"heroSlideUpload_{slide.Id}", slideError);
        }
        if (!ModelState.IsValid)
        {
            await PrepareSettingsViewData(storeId, catalog, posted);
            return View("~/Views/DigitalDownloads/Settings.cshtml", posted);
        }

        if (logoUpload is not null)
        {
            try
            {
                var saved = await files.SaveStorefrontAsset(storeId, "logo", logoUpload, cancellationToken);
                posted.LogoUrl = Url.Action(nameof(DigitalDownloadsPublicController.StorefrontAsset), "DigitalDownloadsPublic", new { storeId, assetId = "logo", fileName = saved.FileName });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(logoUpload), ex.Message);
            }
        }

        foreach (var slide in posted.HeroSlides)
        {
            var upload = NonEmptyUpload(Request.Form.Files, $"heroSlideUpload_{slide.Id}");
            if (upload is null) continue;
            try
            {
                var saved = await files.SaveStorefrontAsset(storeId, $"hero-{slide.Id}", upload, cancellationToken);
                slide.ImageUrl = Url.Action(nameof(DigitalDownloadsPublicController.StorefrontAsset), "DigitalDownloadsPublic", new { storeId, assetId = $"hero-{slide.Id}", fileName = saved.FileName });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError($"heroSlideUpload_{slide.Id}", ex.Message);
            }
        }

        if (!ModelState.IsValid)
        {
            await PrepareSettingsViewData(storeId, catalog, posted);
            return View("~/Views/DigitalDownloads/Settings.cshtml", posted);
        }

        if (faviconUpload is not null)
        {
            try
            {
                var saved = await files.SaveFaviconAsset(storeId, faviconUpload, cancellationToken);
                posted.FaviconUrl = Url.Action(nameof(DigitalDownloadsPublicController.StorefrontAsset), "DigitalDownloadsPublic", new { storeId, assetId = "favicon", fileName = saved.FileName });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(nameof(faviconUpload), ex.Message);
            }
        }

        if (!ModelState.IsValid)
        {
            await PrepareSettingsViewData(storeId, catalog, posted);
            return View("~/Views/DigitalDownloads/Settings.cshtml", posted);
        }

        var primarySlide = posted.HeroSlides.FirstOrDefault(slide => slide.Visible) ?? posted.HeroSlides.FirstOrDefault();
        if (primarySlide is not null)
        {
            posted.HeroEyebrow = primarySlide.Eyebrow;
            posted.HeroHeadline = primarySlide.Headline;
            posted.HeroSubheadline = primarySlide.SupportingCopy;
            posted.HeroImageUrl = primarySlide.ImageUrl;
        }
        await repository.SaveSettings(storeId, posted);
        TempData.SetStatusMessageModel(new StatusMessageModel { Severity = StatusMessageModel.StatusSeverity.Success, Message = "Digital product settings saved." });
        return RedirectToAction(nameof(Settings), new { storeId });
    }

    internal static IFormFile? NonEmptyUpload(IFormFileCollection files, string name) =>
        files.FirstOrDefault(file =>
            file.Name.Equals(name, StringComparison.Ordinal) &&
            file.Length > 0 &&
            !string.IsNullOrWhiteSpace(file.FileName));

    private List<T>? DeserializeEditorState<T>(string? json, string key, int maximum)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            ModelState.AddModelError(key, "The live editor data is missing. Reopen the editor and try again.");
            return null;
        }
        try
        {
            var result = JsonSerializer.Deserialize<List<T>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            if (result.Count > maximum)
            {
                ModelState.AddModelError(key, $"A maximum of {maximum} items is supported.");
                return null;
            }
            return result;
        }
        catch (JsonException)
        {
            ModelState.AddModelError(key, "The live editor data is invalid. Reopen the editor and try again.");
            return null;
        }
    }

    private void ValidateEditorState(DigitalDownloadsSettings posted, IReadOnlySet<string> validProductReferences)
    {
        if (!DigitalStorefrontBuilder.IsSafePublicResourceUrl(posted.LogoUrl)) ModelState.AddModelError(nameof(posted.LogoUrl), "Logo URLs must use HTTP(S) or an uploaded local image.");
        if (!DigitalStorefrontBuilder.IsSafePublicResourceUrl(posted.FaviconUrl)) ModelState.AddModelError(nameof(posted.FaviconUrl), "Favicon URLs must use HTTP(S) or an uploaded local image.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slide in posted.HeroSlides)
        {
            ValidateObject(slide, "Hero slide");
            if (!IsSafeId(slide.Id) || !ids.Add(slide.Id)) ModelState.AddModelError(nameof(posted.HeroSlides), "Every hero slide needs a unique safe identifier.");
            if (!DigitalStorefrontBuilder.IsSafePublicResourceUrl(slide.ImageUrl)) ModelState.AddModelError(nameof(posted.HeroSlides), "Hero images must use an HTTP(S) URL or an uploaded local image.");
            if (!string.IsNullOrWhiteSpace(slide.LinkUrl) && !DigitalStorefrontBuilder.IsSafePublicLink(slide.LinkUrl)) ModelState.AddModelError(nameof(posted.HeroSlides), "Hero links must use HTTP(S), a local path, or an on-page anchor.");
            if (!string.IsNullOrWhiteSpace(slide.ProductReference) && !validProductReferences.Contains(slide.ProductReference)) slide.ProductReference = null;
        }
        if (posted.HeroSlides.Count == 0) ModelState.AddModelError(nameof(posted.HeroSlides), "Add at least one hero slide.");

        ids.Clear();
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in posted.StorefrontCategories)
        {
            ValidateObject(category, "Category");
            if (!IsSafeId(category.Id) || !ids.Add(category.Id)) ModelState.AddModelError(nameof(posted.StorefrontCategories), "Every category needs a unique safe identifier.");
            if (!slugs.Add(category.Slug)) ModelState.AddModelError(nameof(posted.StorefrontCategories), "Category URLs must be unique.");
            category.ProductReferences = (category.ProductReferences ?? []).Where(validProductReferences.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private void ValidateObject(object value, string prefix)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(value, new ValidationContext(value), results, true)) return;
        foreach (var result in results) ModelState.AddModelError(prefix, result.ErrorMessage ?? $"{prefix} is invalid.");
    }

    private static bool IsSafeId(string? value) => value is { Length: > 0 and <= 80 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static void NormalizeProduct(DigitalProduct product)
    {
        if (product.ProductType is DigitalProductType.FileDownload or DigitalProductType.PhotosArt)
            product.DeliveryMode = DigitalDeliveryMode.Download;
        product.WatermarkText = string.IsNullOrWhiteSpace(product.WatermarkText) ? "PREVIEW" : product.WatermarkText.Trim();
        product.PreviewHeading = string.IsNullOrWhiteSpace(product.PreviewHeading) ? PreviewLabel(product.ProductType, 0) : product.PreviewHeading.Trim();
    }

    private void ValidateProductConfiguration(DigitalProduct product)
    {
        if (!DigitalStorefrontBuilder.IsSafePublicResourceUrl(product.ImageUrl))
            ModelState.AddModelError(nameof(product.ImageUrl), "Cover URLs must use HTTP(S) or an uploaded local image.");
        if (product.DeliveryMode != DigitalDeliveryMode.Download && product.ProductType is DigitalProductType.FileDownload or DigitalProductType.PhotosArt)
            ModelState.AddModelError(nameof(product.DeliveryMode), "File and photo products are delivered as protected downloads.");
        if (product.DeliveryMode != DigitalDeliveryMode.Download)
        {
            var validStreamType = product.ProductType switch
            {
                DigitalProductType.PdfEbook => product.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase),
                DigitalProductType.Audio => product.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase),
                DigitalProductType.Video => product.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
            if (!validStreamType) ModelState.AddModelError(nameof(product.DeliveryMode), "Streaming requires a PDF, browser-compatible audio file, or browser-compatible video file.");
        }
        if (product.ProductType == DigitalProductType.PdfEbook && product.StorageLocation is not null && !product.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(product.ContentType), "PDF / ebook products require a PDF protected file.");
        if (product.ProductType == DigitalProductType.Audio && product.StorageLocation is not null && !product.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(product.ContentType), "Music & audio products require an audio protected file.");
        if (product.ProductType == DigitalProductType.Video && product.StorageLocation is not null && !product.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(product.ContentType), "Video products require a video protected file.");
    }

    private static string PreviewLabel(DigitalProductType type, int index) => type switch
    {
        DigitalProductType.PdfEbook => "Book preview",
        DigitalProductType.Audio => "Audio demo",
        DigitalProductType.Video => "Video trailer",
        DigitalProductType.PhotosArt => $"Gallery image {index + 1}",
        _ => "Product sample"
    };

    private async Task PrepareSettingsViewData(
        string storeId,
        IReadOnlyList<StoreProductViewModel>? catalog = null,
        DigitalDownloadsSettings? settings = null)
    {
        settings ??= await repository.GetSettings(storeId);
        catalog ??= checkoutService.BuildCatalog(await repository.GetProducts(storeId), await licenses.GetProducts(storeId));
        ViewData["StoreId"] = storeId;
        var legacyPath = Url.Action(nameof(DigitalDownloadsPublicController.Storefront), "DigitalDownloadsPublic", new { storeId }) ?? DigitalPublicUrlService.LegacyPrefix(storeId);
        var canonical = await publicUrls.Absolute(storeId, Request.GetAbsoluteRoot(), legacyPath);
        var apps = await digitalApps.GetForStore(storeId);
        var (mappedApp, mappedDomain) = await digitalApps.MappingForStore(storeId);
        ViewData["PreviewStoreUrl"] = canonical;
        ViewData["CanonicalStoreUrl"] = canonical;
        ViewData["DigitalProductsApps"] = apps;
        ViewData["MappedDigitalProductsApp"] = mappedDomain is null ? null : mappedApp;
        ViewData["MappedDigitalProductsDomain"] = mappedDomain;
        ViewData["CreateDigitalProductsAppUrl"] = Url.Action("CreateApp", "UIApps",
            new { storeId, appType = DigitalProductsAppType.AppType }) ??
            $"{Request.PathBase}/stores/{storeId}/apps/create/{DigitalProductsAppType.AppType}";
        ViewData["CatalogOptions"] = catalog;
        ViewData.SetActivePage("DigitalDownloadsSettings", "Digital Products", "Storefront & delivery settings");
    }

    [HttpGet("orders/{orderId}")]
    public async Task<IActionResult> OrderDetail(
        string storeId,
        string orderId,
        string? section = null,
        string? orderSearch = null,
        string? orderProductId = null,
        string? orderStatus = null,
        int orderPage = 1,
        int orderPageSize = 25)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();

        var digitalOrder = await repository.GetOrder(storeId, orderId);
        var licenseOrder = digitalOrder is null ? await licenses.GetOrder(storeId, orderId) : null;
        var checkoutId = digitalOrder?.CheckoutId ?? licenseOrder?.CheckoutId;
        var checkout = !string.IsNullOrWhiteSpace(checkoutId)
            ? await repository.GetCheckout(storeId, checkoutId)
            : await repository.GetCheckout(storeId, orderId);
        if (digitalOrder is null && licenseOrder is null && checkout is null) return NotFound();

        var model = DigitalOrderDetailBuilder.Build(
            storeId,
            orderId,
            digitalOrder,
            licenseOrder,
            checkout,
            await repository.GetOrders(storeId),
            await repository.GetProducts(storeId),
            await licenses.GetOrders(storeId),
            await licenses.GetProducts(storeId),
            await licenses.GetLicenses(storeId),
            section,
            orderSearch,
            orderProductId,
            orderStatus,
            orderPage,
            orderPageSize);
        ViewData.SetActivePage("DigitalDownloads", "Digital Products", $"Order {ShortId(orderId)}");
        return View("~/Views/DigitalDownloads/OrderDetail.cshtml", model);
    }

    [HttpPost("orders/{orderId}/revoke")]
    public async Task<IActionResult> Revoke(
        string storeId,
        string orderId,
        string? orderSearch = null,
        string? orderProductId = null,
        string? orderStatus = null,
        int orderPage = 1,
        int orderPageSize = 25,
        bool returnToDetail = false,
        string? detailOrderId = null,
        string? section = null)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var revoked = await repository.UpdateOrder(storeId, orderId, order =>
        {
            order.Status = DigitalOrderStatus.Revoked;
            order.TokenHash = null;
            order.ProtectedToken = null;
            return true;
        });
        if (revoked is null) return NotFound();
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = "Protected access revoked."
        });
        if (returnToDetail)
        {
            return RedirectToAction(nameof(OrderDetail), new
            {
                storeId,
                orderId = string.IsNullOrWhiteSpace(detailOrderId) ? orderId : detailOrderId,
                section = string.Equals(section, "licenses", StringComparison.OrdinalIgnoreCase) ? "licenses" : "products",
                orderSearch,
                orderProductId,
                orderStatus,
                orderPage,
                orderPageSize
            });
        }
        var dashboardUrl = Url.Action(nameof(Index), new
        {
            storeId,
            section = "products",
            orderSearch,
            orderProductId,
            orderStatus,
            orderPage,
            orderPageSize
        });
        return Redirect($"{dashboardUrl}#orders");
    }

    private static string ShortId(string value) => value[..Math.Min(8, value.Length)];
}
