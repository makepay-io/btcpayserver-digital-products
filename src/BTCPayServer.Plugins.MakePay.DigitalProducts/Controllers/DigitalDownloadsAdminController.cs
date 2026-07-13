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
    DownloadTokenService secrets) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        ViewData.SetActivePage("DigitalDownloads", "Digital Downloads", "Digital Downloads");
        return View("~/Views/DigitalDownloads/Index.cshtml", new DigitalDownloadsDashboardViewModel
        {
            StoreId = storeId,
            Settings = await repository.GetSettings(storeId),
            Products = await repository.GetProducts(storeId),
            Orders = (await repository.GetOrders(storeId)).Take(100).ToList()
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
        ViewData.SetActivePage("DigitalDownloads", "Digital Downloads", product.Id.Length == 0 ? "New product" : product.Name);
        return View("~/Views/DigitalDownloads/Product.cshtml", product);
    }

    [HttpPost("products/{productId}")]
    public async Task<IActionResult> SaveProduct(string storeId, string productId, DigitalProduct posted, IFormFile? upload, CancellationToken cancellationToken)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var existing = productId == "new" ? null : await repository.GetProduct(storeId, productId);
        posted.Id = existing?.Id ?? Guid.NewGuid().ToString("N");
        posted.CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;
        if (upload is not null)
        {
            var saved = await files.SaveLocal(storeId, posted.Id, upload, cancellationToken);
            posted.StorageKind = ProductStorageKind.Local;
            posted.StorageLocation = saved.RelativePath;
            posted.DownloadFileName = saved.FileName;
            posted.ContentType = saved.ContentType;
            posted.FileSize = saved.Size;
        }
        else if (posted.StorageKind == ProductStorageKind.Local && existing is not null)
        {
            posted.StorageLocation = existing.StorageLocation;
            posted.DownloadFileName = existing.DownloadFileName;
            posted.ContentType = existing.ContentType;
            posted.FileSize = existing.FileSize;
        }
        if (!ModelState.IsValid)
        {
            ViewData["StoreId"] = storeId;
            return View("~/Views/DigitalDownloads/Product.cshtml", posted);
        }
        try { await repository.SaveProduct(storeId, posted); }
        catch (InvalidOperationException ex) { ModelState.AddModelError(nameof(posted.Slug), ex.Message); ViewData["StoreId"] = storeId; return View("~/Views/DigitalDownloads/Product.cshtml", posted); }
        TempData.SetStatusMessageModel(new StatusMessageModel { Severity = StatusMessageModel.StatusSeverity.Success, Message = "Product saved." });
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpPost("products/{productId}/delete")]
    public async Task<IActionResult> DeleteProduct(string storeId, string productId)
    {
        await repository.DeleteProduct(storeId, productId);
        TempData.SetStatusMessageModel(new StatusMessageModel { Severity = StatusMessageModel.StatusSeverity.Success, Message = "Product removed from the catalog." });
        return RedirectToAction(nameof(Index), new { storeId });
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        await PrepareSettingsViewData(storeId);
        return View("~/Views/DigitalDownloads/Settings.cshtml", await repository.GetSettings(storeId));
    }

    [HttpPost("settings")]
    public async Task<IActionResult> SaveSettings(
        string storeId,
        DigitalDownloadsSettings posted,
        string? heroSlidesJson,
        string? categoriesJson,
        IFormFile? logoUpload,
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

        ValidateEditorState(posted, validProductReferences);
        posted.ProtectedS3SecretKey = string.IsNullOrWhiteSpace(s3SecretKey) ? existing.ProtectedS3SecretKey : secrets.ProtectSecret(s3SecretKey);
        posted.ProtectedRemoteAuthorizationValue = string.IsNullOrWhiteSpace(remoteAuthorizationValue) ? existing.ProtectedRemoteAuthorizationValue : secrets.ProtectSecret(remoteAuthorizationValue);
        posted.CustomerAccountsEnabled = true;
        if (posted.DefaultDownloadLimit < 1 || posted.DefaultLinkHours < 1) ModelState.AddModelError("", "Download limit and link lifetime must be at least one.");
        if (!string.IsNullOrWhiteSpace(posted.RemoteAuthorizationHeader) && !ProductFileService.IsSafeHeaderName(posted.RemoteAuthorizationHeader)) ModelState.AddModelError(nameof(posted.RemoteAuthorizationHeader), "Use a valid HTTP header name; connection, cookie, and framing headers are not allowed.");
        if (!string.IsNullOrWhiteSpace(remoteAuthorizationValue) && (remoteAuthorizationValue.Contains('\r') || remoteAuthorizationValue.Contains('\n'))) ModelState.AddModelError("remoteAuthorizationValue", "Authorization values cannot contain line breaks.");
        if (!ModelState.IsValid)
        {
            await PrepareSettingsViewData(storeId, catalog);
            return View("~/Views/DigitalDownloads/Settings.cshtml", posted);
        }

        if (logoUpload is not null && ProductFileService.ValidateStorefrontAsset(logoUpload) is { } logoError)
            ModelState.AddModelError(nameof(logoUpload), logoError);
        foreach (var slide in posted.HeroSlides)
        {
            var upload = Request.Form.Files.FirstOrDefault(file => file.Name.Equals($"heroSlideUpload_{slide.Id}", StringComparison.Ordinal));
            if (upload is not null && ProductFileService.ValidateStorefrontAsset(upload) is { } slideError)
                ModelState.AddModelError($"heroSlideUpload_{slide.Id}", slideError);
        }
        if (!ModelState.IsValid)
        {
            await PrepareSettingsViewData(storeId, catalog);
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
            var upload = Request.Form.Files.FirstOrDefault(file => file.Name.Equals($"heroSlideUpload_{slide.Id}", StringComparison.Ordinal));
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
            await PrepareSettingsViewData(storeId, catalog);
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
        TempData.SetStatusMessageModel(new StatusMessageModel { Severity = StatusMessageModel.StatusSeverity.Success, Message = "Digital download settings saved." });
        return RedirectToAction(nameof(Settings), new { storeId });
    }

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

    private async Task PrepareSettingsViewData(string storeId, IReadOnlyList<StoreProductViewModel>? catalog = null)
    {
        catalog ??= checkoutService.BuildCatalog(await repository.GetProducts(storeId), await licenses.GetProducts(storeId));
        ViewData["StoreId"] = storeId;
        ViewData["PreviewStoreUrl"] = Url.Action(nameof(DigitalDownloadsPublicController.Storefront), "DigitalDownloadsPublic", new { storeId });
        ViewData["CatalogOptions"] = catalog;
        ViewData.SetActivePage("DigitalDownloadsSettings", "Digital Downloads", "Storefront & delivery settings");
    }

    [HttpPost("orders/{orderId}/revoke")]
    public async Task<IActionResult> Revoke(string storeId, string orderId)
    {
        await repository.UpdateOrder(storeId, orderId, order => { order.Status = DigitalOrderStatus.Revoked; order.TokenHash = null; order.ProtectedToken = null; return true; });
        return RedirectToAction(nameof(Index), new { storeId });
    }
}
