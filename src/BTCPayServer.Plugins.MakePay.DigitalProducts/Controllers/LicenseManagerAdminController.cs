#nullable enable
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;
using BTCPayServer.Plugins.MakePay.LicenseManager.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.MakePay.LicenseManager.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
[AutoValidateAntiforgeryToken]
[Route("plugins/{storeId}/license-manager")]
public sealed class LicenseManagerAdminController(StoreRepository stores, LicenseRepository repository, LicenseKeyGenerator generator, LicenseSecurityService security, LicenseFulfillmentService fulfillment) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        ViewData.SetActivePage("DigitalDownloads", "Digital Products", "License keys");
        return View("~/Views/LicenseManager/Index.cshtml", new LicenseDashboardViewModel { StoreId = storeId, Settings = await repository.GetSettings(storeId), Products = await repository.GetProducts(storeId), Licenses = (await repository.GetLicenses(storeId)).Take(200).ToList(), Orders = (await repository.GetOrders(storeId)).Take(100).ToList() });
    }
    [HttpGet("products/new")][HttpGet("products/{productId}")]
    public async Task<IActionResult> Product(string storeId, string? productId)
    {
        var product = string.IsNullOrWhiteSpace(productId) ? new LicenseProduct() : await repository.GetProduct(storeId, productId); if (product is null) return NotFound(); ViewData["StoreId"] = storeId; return View("~/Views/LicenseManager/Product.cshtml", product);
    }
    [HttpPost("products/{productId}")]
    public async Task<IActionResult> SaveProduct(string storeId, string productId, LicenseProduct posted)
    {
        var existing = productId == "new" ? null : await repository.GetProduct(storeId, productId); posted.Id = existing?.Id ?? Guid.NewGuid().ToString("N"); posted.CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;
        try { generator.Validate(posted.KeyPattern); } catch (FormatException ex) { ModelState.AddModelError(nameof(posted.KeyPattern), ex.Message); }
        if (!ModelState.IsValid) { ViewData["StoreId"] = storeId; return View("~/Views/LicenseManager/Product.cshtml", posted); }
        try { await repository.SaveProduct(storeId, posted); } catch (InvalidOperationException ex) { ModelState.AddModelError(nameof(posted.Slug), ex.Message); ViewData["StoreId"] = storeId; return View("~/Views/LicenseManager/Product.cshtml", posted); }
        TempData.SetStatusMessageModel(new() { Severity = StatusMessageModel.StatusSeverity.Success, Message = "License product saved." }); return RedirectToAction(nameof(Index), new { storeId });
    }
    [HttpGet("settings")]
    public async Task<IActionResult> Settings(string storeId) { if (await stores.FindStore(storeId) is null) return NotFound(); ViewData["StoreId"] = storeId; return View("~/Views/LicenseManager/Settings.cshtml", await repository.GetSettings(storeId)); }
    [HttpPost("settings")]
    public async Task<IActionResult> SaveSettings(string storeId, LicenseManagerSettings posted, string? apiSecret)
    {
        posted.FaviconUrl = DigitalStorefrontBuilder.NormalizePublicResourceUrl(posted.FaviconUrl);
        var existing = await repository.GetSettings(storeId); posted.ProtectedApiSecret = string.IsNullOrWhiteSpace(apiSecret) ? existing.ProtectedApiSecret : security.Protect(apiSecret);
        if (!DigitalStorefrontBuilder.IsSafePublicResourceUrl(posted.FaviconUrl)) ModelState.AddModelError(nameof(posted.FaviconUrl), "Favicon URLs must use HTTP(S) or an uploaded local image.");
        if (!LicenseSecurityService.ValidHeaderConfiguration(posted.LicenseKeyHeader, posted.SignatureHeader, posted.TimestampHeader, posted.NonceHeader, posted.ResponseSignatureHeader)) ModelState.AddModelError("", "API header names must be unique, valid custom X- headers.");
        if (!ModelState.IsValid) { ViewData["StoreId"] = storeId; return View("~/Views/LicenseManager/Settings.cshtml", posted); }
        await repository.SaveSettings(storeId, posted); TempData.SetStatusMessageModel(new() { Severity = StatusMessageModel.StatusSeverity.Success, Message = "License API settings saved." }); return RedirectToAction(nameof(Settings), new { storeId });
    }
    [HttpPost("settings/generate-secret")]
    public async Task<IActionResult> GenerateSecret(string storeId)
    {
        var value = LicenseSecurityService.GenerateApiSecret(); var settings = await repository.GetSettings(storeId); settings.ProtectedApiSecret = security.Protect(value); await repository.SaveSettings(storeId, settings); TempData["LicenseApiSecret"] = value; return RedirectToAction(nameof(Settings), new { storeId });
    }
    [HttpPost("licenses/issue")]
    public async Task<IActionResult> Issue(string storeId, string productId, string email)
    {
        var product = await repository.GetProduct(storeId, productId); if (product is null) return NotFound(); var license = await fulfillment.Issue(storeId, product, email); TempData["IssuedLicenseKey"] = security.Unprotect(license.ProtectedKey); return RedirectToAction(nameof(Index), new { storeId });
    }
    [HttpPost("licenses/{licenseId}/status")]
    public async Task<IActionResult> Status(string storeId, string licenseId, ManagedLicenseStatus status) { await repository.UpdateLicense(storeId, licenseId, license => { license.Status = status; license.Audit.Add(new() { Action = "admin_status", Success = true, Detail = status.ToString() }); return true; }); return RedirectToAction(nameof(Index), new { storeId }); }
}
