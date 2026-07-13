#nullable enable
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
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
        ViewData["StoreId"] = storeId;
        ViewData.SetActivePage("DigitalDownloadsSettings", "Digital Downloads", "Storefront & delivery settings");
        return View("~/Views/DigitalDownloads/Settings.cshtml", await repository.GetSettings(storeId));
    }

    [HttpPost("settings")]
    public async Task<IActionResult> SaveSettings(string storeId, DigitalDownloadsSettings posted, string? s3SecretKey, string? remoteAuthorizationValue)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var existing = await repository.GetSettings(storeId);
        posted.ProtectedS3SecretKey = string.IsNullOrWhiteSpace(s3SecretKey) ? existing.ProtectedS3SecretKey : secrets.ProtectSecret(s3SecretKey);
        posted.ProtectedRemoteAuthorizationValue = string.IsNullOrWhiteSpace(remoteAuthorizationValue) ? existing.ProtectedRemoteAuthorizationValue : secrets.ProtectSecret(remoteAuthorizationValue);
        if (posted.DefaultDownloadLimit < 1 || posted.DefaultLinkHours < 1) ModelState.AddModelError("", "Download limit and link lifetime must be at least one.");
        if (!string.IsNullOrWhiteSpace(posted.RemoteAuthorizationHeader) && !ProductFileService.IsSafeHeaderName(posted.RemoteAuthorizationHeader)) ModelState.AddModelError(nameof(posted.RemoteAuthorizationHeader), "Use a valid HTTP header name; connection, cookie, and framing headers are not allowed.");
        if (!string.IsNullOrWhiteSpace(remoteAuthorizationValue) && (remoteAuthorizationValue.Contains('\r') || remoteAuthorizationValue.Contains('\n'))) ModelState.AddModelError("remoteAuthorizationValue", "Authorization values cannot contain line breaks.");
        if (!ModelState.IsValid) { ViewData["StoreId"] = storeId; return View("~/Views/DigitalDownloads/Settings.cshtml", posted); }
        await repository.SaveSettings(storeId, posted);
        TempData.SetStatusMessageModel(new StatusMessageModel { Severity = StatusMessageModel.StatusSeverity.Success, Message = "Digital download settings saved." });
        return RedirectToAction(nameof(Settings), new { storeId });
    }

    [HttpPost("orders/{orderId}/revoke")]
    public async Task<IActionResult> Revoke(string storeId, string orderId)
    {
        await repository.UpdateOrder(storeId, orderId, order => { order.Status = DigitalOrderStatus.Revoked; order.TokenHash = null; order.ProtectedToken = null; return true; });
        return RedirectToAction(nameof(Index), new { storeId });
    }
}
