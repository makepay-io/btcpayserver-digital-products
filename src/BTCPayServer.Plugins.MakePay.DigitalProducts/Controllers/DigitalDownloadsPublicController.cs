#nullable enable
using System.ComponentModel.DataAnnotations;
using System.Net;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using BTCPayServer.Plugins.MakePay.LicenseManager.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Controllers;

[Route("stores/{storeId}/downloads")]
public sealed class DigitalDownloadsPublicController(
    StoreRepository stores,
    DigitalDownloadsRepository repository,
    LicenseRepository licenses,
    UIInvoiceController invoices,
    ProductFileService files,
    DownloadTokenService tokens,
    IRateLimitService rateLimits) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Storefront(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var products = (await repository.GetProducts(storeId)).Where(product => product.Active).ToList();
        return View("~/Views/DigitalDownloads/Public/Storefront.cshtml", new StorefrontViewModel
        {
            StoreId = storeId,
            Settings = await repository.GetSettings(storeId),
            Products = products,
            LicenseSettings = await licenses.GetSettings(storeId),
            LicenseProducts = (await licenses.GetProducts(storeId)).Where(product => product.Active).ToList()
        });
    }

    [HttpPost("buy/{productId}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Buy(string storeId, string productId, [FromForm, EmailAddress] string email, CancellationToken cancellationToken)
    {
        var zone = $"makepay-dd-buy-{storeId}-{HttpContext.Connection.RemoteIpAddress}";
        if (!await rateLimits.Throttle(ZoneLimits.PublicInvoices, zone, cancellationToken)) return StatusCode(StatusCodes.Status429TooManyRequests);
        var store = await stores.FindStore(storeId);
        var product = await repository.GetProduct(storeId, productId);
        if (store is null || product is null || !product.Active) return NotFound();
        if (!new EmailAddressAttribute().IsValid(email)) return BadRequest("A valid delivery email is required.");
        var settings = await repository.GetSettings(storeId);
        var order = new DigitalOrder { StoreId = storeId, ProductId = product.Id, BuyerEmail = email.Trim(), PublicBaseUrl = Request.GetAbsoluteRoot(), Status = DigitalOrderStatus.Pending };
        await repository.SaveOrder(storeId, order);
        try
        {
            var successUrl = Url.ActionLink(nameof(Order), values: new { storeId, orderId = order.Id })!;
            var invoice = await invoices.CreateInvoiceCoreRaw(new CreateInvoiceRequest
            {
                Amount = product.Price,
                Currency = settings.Currency,
                Metadata = new InvoiceMetadata { BuyerEmail = order.BuyerEmail, ItemCode = product.Id, ItemDesc = product.Name, OrderId = order.Id, OrderUrl = Request.GetDisplayUrl() }.ToJObject(),
                Checkout = new InvoiceDataBase.CheckoutOptions { RedirectAutomatically = true, RedirectURL = successUrl }
            }, store, Request.GetAbsoluteRoot(), [DigitalDeliveryService.Tag(order.Id)], cancellationToken);
            order.InvoiceId = invoice.Id;
            await repository.SaveOrder(storeId, order);
            return RedirectToAction(nameof(UIInvoiceController.Checkout), "UIInvoice", new { invoiceId = invoice.Id });
        }
        catch
        {
            await repository.DeleteOrder(storeId, order.Id);
            throw;
        }
    }

    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> Order(string storeId, string orderId)
    {
        var order = await repository.GetOrder(storeId, orderId);
        if (order is null) return NotFound();
        var product = await repository.GetProduct(storeId, order.ProductId);
        if (product is null) return NotFound();
        string? downloadUrl = null;
        if (order.Status == DigitalOrderStatus.Paid && tokens.Unprotect(order.ProtectedToken) is { } token)
            downloadUrl = Url.ActionLink(nameof(Download), values: new { storeId, orderId, token });
        return View("~/Views/DigitalDownloads/Public/Order.cshtml", new OrderViewModel { Settings = await repository.GetSettings(storeId), Product = product, Order = order, DownloadUrl = downloadUrl });
    }

    [HttpGet("order/{orderId}/file")]
    public async Task<IActionResult> Download(string storeId, string orderId, string token, CancellationToken cancellationToken)
    {
        var order = await repository.GetOrder(storeId, orderId);
        if (order is null || order.Status != DigitalOrderStatus.Paid || string.IsNullOrWhiteSpace(order.TokenHash) || !tokens.Verify(token, order.TokenHash)) return NotFound();
        if (order.ExpiresAt <= DateTimeOffset.UtcNow || order.DownloadCount >= order.MaxDownloads) return StatusCode(StatusCodes.Status410Gone, "This download link has expired or reached its download limit.");
        var settings = await repository.GetSettings(storeId);
        var ipHash = DownloadTokenService.Hash(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (settings.LockToFirstIp && order.FirstIpHash is not null && order.FirstIpHash != ipHash) return StatusCode(StatusCodes.Status403Forbidden, "This link is locked to its first network address.");
        var product = await repository.GetProduct(storeId, order.ProductId);
        if (product is null) return NotFound();
        var remote = await files.Open(product, settings, cancellationToken);
        var updated = await repository.UpdateOrder(storeId, orderId, current =>
        {
            if (current.Status != DigitalOrderStatus.Paid || current.DownloadCount >= current.MaxDownloads || current.ExpiresAt <= DateTimeOffset.UtcNow) return false;
            current.FirstIpHash ??= settings.LockToFirstIp ? ipHash : null;
            current.DownloadCount++;
            current.LastDownloadAt = DateTimeOffset.UtcNow;
            return true;
        });
        if (updated is null) { await remote.DisposeAsync(); return StatusCode(StatusCodes.Status410Gone); }
        Response.RegisterForDisposeAsync(remote);
        Response.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileNameStar = product.DownloadFileName }.ToString();
        if (remote.Length is { } length) Response.ContentLength = length;
        return File(remote.Stream, remote.ContentType, enableRangeProcessing: product.StorageKind == ProductStorageKind.Local);
    }
}
