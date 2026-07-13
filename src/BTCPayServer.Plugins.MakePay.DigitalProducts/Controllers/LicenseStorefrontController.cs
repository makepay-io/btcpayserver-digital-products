#nullable enable
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;
using BTCPayServer.Plugins.MakePay.LicenseManager.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.MakePay.LicenseManager.Controllers;

[Route("stores/{storeId}/licenses")]
public sealed class LicenseStorefrontController(StoreRepository stores, LicenseRepository repository, UIInvoiceController invoices, LicenseSecurityService security) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Storefront(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        return RedirectToAction("Storefront", "DigitalDownloadsPublic", new { storeId });
    }
    [HttpPost("buy/{productId}")][IgnoreAntiforgeryToken]
    public async Task<IActionResult> Buy(string storeId, string productId, [FromForm, EmailAddress] string email, CancellationToken cancellationToken)
    {
        var store = await stores.FindStore(storeId); var product = await repository.GetProduct(storeId, productId); if (store is null || product is null || !product.Active) return NotFound(); if (!new EmailAddressAttribute().IsValid(email)) return BadRequest("Valid email required.");
        var settings = await repository.GetSettings(storeId); var order = new LicenseOrder { StoreId = storeId, ProductId = product.Id, BuyerEmail = email.Trim() }; await repository.SaveOrder(storeId, order);
        try { var success = Url.ActionLink(nameof(Order), values: new { storeId, orderId = order.Id })!; var invoice = await invoices.CreateInvoiceCoreRaw(new CreateInvoiceRequest { Amount = product.Price, Currency = settings.Currency, Metadata = new InvoiceMetadata { BuyerEmail = email, ItemCode = product.Id, ItemDesc = product.Name, OrderId = order.Id, OrderUrl = Request.GetDisplayUrl() }.ToJObject(), Checkout = new InvoiceDataBase.CheckoutOptions { RedirectAutomatically = true, RedirectURL = success } }, store, Request.GetAbsoluteRoot(), [LicenseFulfillmentService.Tag(order.Id)], cancellationToken); order.InvoiceId = invoice.Id; await repository.SaveOrder(storeId, order); return RedirectToAction(nameof(UIInvoiceController.Checkout), "UIInvoice", new { invoiceId = invoice.Id }); }
        catch { await repository.DeleteOrder(storeId, order.Id); throw; }
    }
    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> Order(string storeId, string orderId)
    {
        var order = await repository.GetOrder(storeId, orderId); if (order is null) return NotFound(); var product = await repository.GetProduct(storeId, order.ProductId); if (product is null) return NotFound(); var license = order.LicenseId is null ? null : await repository.GetLicense(storeId, order.LicenseId); return View("~/Views/LicenseManager/Public/Order.cshtml", new LicenseOrderViewModel { Settings = await repository.GetSettings(storeId), Product = product, Order = order, LicenseKey = license is null ? null : security.Unprotect(license.ProtectedKey), ExpiresAt = license?.ExpiresAt });
    }
}
