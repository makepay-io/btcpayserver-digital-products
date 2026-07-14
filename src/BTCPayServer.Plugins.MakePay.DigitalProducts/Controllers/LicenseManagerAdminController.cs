#nullable enable
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Controllers;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;
using BTCPayServer.Plugins.MakePay.LicenseManager.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.MakePay.LicenseManager.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
[AutoValidateAntiforgeryToken]
[Route("plugins/{storeId}/digital-downloads/licenses")]
public sealed class LicenseManagerAdminController(
    StoreRepository stores,
    LicenseRepository repository,
    LicenseKeyGenerator generator,
    LicenseSecurityService security,
    LicenseFulfillmentService fulfillment) : Controller
{
    [HttpGet("")]
    public IActionResult Index(string storeId) => RedirectToUnifiedDashboard(storeId);

    [HttpGet("products/new")]
    [HttpGet("products/{productId}")]
    public async Task<IActionResult> Product(string storeId, string? productId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var product = string.IsNullOrWhiteSpace(productId)
            ? new LicenseProduct()
            : await repository.GetProduct(storeId, productId);
        if (product is null) return NotFound();
        ViewData["StoreId"] = storeId;
        ViewData.SetActivePage("DigitalDownloads", "Digital Products", productId is null ? "New license product" : product.Name);
        return View("~/Views/LicenseManager/Product.cshtml", product);
    }

    [HttpPost("products/{productId}")]
    [HttpPost("~/plugins/{storeId}/license-manager/products/{productId}")]
    public async Task<IActionResult> SaveProduct(string storeId, string productId, LicenseProduct posted)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var existing = productId == "new" ? null : await repository.GetProduct(storeId, productId);
        posted.Id = existing?.Id ?? Guid.NewGuid().ToString("N");
        posted.CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow;
        try
        {
            generator.Validate(posted.KeyPattern);
        }
        catch (FormatException ex)
        {
            ModelState.AddModelError(nameof(posted.KeyPattern), ex.Message);
        }

        if (!ModelState.IsValid)
        {
            ViewData["StoreId"] = storeId;
            return View("~/Views/LicenseManager/Product.cshtml", posted);
        }

        try
        {
            await repository.SaveProduct(storeId, posted);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(posted.Slug), ex.Message);
            ViewData["StoreId"] = storeId;
            return View("~/Views/LicenseManager/Product.cshtml", posted);
        }

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = "License product saved."
        });
        return RedirectToUnifiedDashboard(storeId);
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        ViewData["StoreId"] = storeId;
        ViewData.SetActivePage("DigitalDownloads", "Digital Products", "License API & delivery");
        return View("~/Views/LicenseManager/Settings.cshtml", await repository.GetSettings(storeId));
    }

    [HttpPost("settings")]
    [HttpPost("~/plugins/{storeId}/license-manager/settings")]
    public async Task<IActionResult> SaveSettings(string storeId, LicenseManagerSettings posted, string? apiSecret)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        posted.FaviconUrl = DigitalStorefrontBuilder.NormalizePublicResourceUrl(posted.FaviconUrl);
        var existing = await repository.GetSettings(storeId);
        posted.ProtectedApiSecret = string.IsNullOrWhiteSpace(apiSecret)
            ? existing.ProtectedApiSecret
            : security.Protect(apiSecret);
        if (!DigitalStorefrontBuilder.IsSafePublicResourceUrl(posted.FaviconUrl))
            ModelState.AddModelError(nameof(posted.FaviconUrl), "Favicon URLs must use HTTP(S) or an uploaded local image.");
        if (!LicenseSecurityService.ValidHeaderConfiguration(
                posted.LicenseKeyHeader,
                posted.SignatureHeader,
                posted.TimestampHeader,
                posted.NonceHeader,
                posted.ResponseSignatureHeader))
            ModelState.AddModelError("", "API header names must be unique, valid custom X- headers.");
        if (!ModelState.IsValid)
        {
            ViewData["StoreId"] = storeId;
            return View("~/Views/LicenseManager/Settings.cshtml", posted);
        }

        await repository.SaveSettings(storeId, posted);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = "License API settings saved."
        });
        return RedirectToAction(nameof(Settings), new { storeId });
    }

    [HttpPost("settings/generate-secret")]
    [HttpPost("~/plugins/{storeId}/license-manager/settings/generate-secret")]
    public async Task<IActionResult> GenerateSecret(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var value = LicenseSecurityService.GenerateApiSecret();
        var settings = await repository.GetSettings(storeId);
        settings.ProtectedApiSecret = security.Protect(value);
        await repository.SaveSettings(storeId, settings);
        TempData["LicenseApiSecret"] = value;
        return RedirectToAction(nameof(Settings), new { storeId });
    }

    [HttpPost("issue")]
    [HttpPost("~/plugins/{storeId}/license-manager/licenses/issue")]
    public async Task<IActionResult> Issue(string storeId, string productId, string email)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var product = await repository.GetProduct(storeId, productId);
        if (product is null) return NotFound();
        var license = await fulfillment.Issue(storeId, product, email);
        TempData["IssuedLicenseKey"] = security.Unprotect(license.ProtectedKey);
        return RedirectToUnifiedDashboard(storeId);
    }

    [HttpPost("{licenseId}/status")]
    [HttpPost("~/plugins/{storeId}/license-manager/licenses/{licenseId}/status")]
    public async Task<IActionResult> Status(string storeId, string licenseId, ManagedLicenseStatus? status)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        if (!TryNormalizeStatus(status, out var requestedStatus))
            return BadRequest("A valid license status is required.");
        await repository.UpdateLicense(storeId, licenseId, license =>
        {
            license.Status = requestedStatus;
            license.Audit.Add(new LicenseAuditEvent
            {
                Action = "admin_status",
                Success = true,
                Detail = requestedStatus.ToString()
            });
            return true;
        });
        return RedirectToUnifiedDashboard(storeId);
    }

    internal static bool TryNormalizeStatus(ManagedLicenseStatus? requested, out ManagedLicenseStatus status)
    {
        if (requested.HasValue && Enum.IsDefined(requested.Value))
        {
            status = requested.Value;
            return true;
        }

        status = default;
        return false;
    }

    private RedirectToActionResult RedirectToUnifiedDashboard(string storeId) =>
        RedirectToAction(nameof(DigitalDownloadsAdminController.Index), "DigitalDownloadsAdmin", new
        {
            storeId,
            section = "licenses"
        });
}

/// <summary>
/// Keeps existing admin bookmarks working while all license management is presented
/// inside the Digital Products experience. Public storefront and API controllers are
/// intentionally separate and are not affected by these redirects.
/// </summary>
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
[AutoValidateAntiforgeryToken]
[Route("plugins/{storeId}/license-manager")]
public sealed class LegacyLicenseManagerAdminController : Controller
{
    [HttpGet("")]
    public IActionResult Index(string storeId) => RedirectToUnifiedDashboard(storeId);

    [HttpGet("products/new")]
    [HttpGet("products/{productId}")]
    public IActionResult Product(string storeId, string? productId) =>
        RedirectToAction(nameof(LicenseManagerAdminController.Product), "LicenseManagerAdmin", new { storeId, productId });

    [HttpGet("settings")]
    public IActionResult Settings(string storeId) =>
        RedirectToAction(nameof(LicenseManagerAdminController.Settings), "LicenseManagerAdmin", new { storeId });

    private RedirectToActionResult RedirectToUnifiedDashboard(string storeId) =>
        RedirectToAction(nameof(DigitalDownloadsAdminController.Index), "DigitalDownloadsAdmin", new
        {
            storeId,
            section = "licenses"
        });
}
