#nullable enable
using System.Text.Encodings.Web;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BTCPayServer.Plugins.MakePay.LicenseManager.Services;

public sealed class LicenseFulfillmentService(EventAggregator events, ILogger<LicenseFulfillmentService> logger, LicenseRepository repository, LicenseKeyGenerator generator, LicenseSecurityService security, EmailSenderFactory emailFactory) : EventHostedServiceBase(events, logger)
{
    public const string TagPrefix = "MPLM#";
    public static string Tag(string orderId) => TagPrefix + orderId;
    protected override void SubscribeToEvents() => Subscribe<InvoiceEvent>();
    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is not InvoiceEvent invoiceEvent) return;
        var orderId = invoiceEvent.Invoice.GetInternalTags(TagPrefix).FirstOrDefault(); if (orderId is null) return;
        var settings = await repository.GetSettings(invoiceEvent.Invoice.StoreId);
        var eligible = invoiceEvent.EventCode is InvoiceEventCode.Completed or InvoiceEventCode.Confirmed or InvoiceEventCode.MarkedCompleted || settings.DeliverOnProcessing && invoiceEvent.EventCode == InvoiceEventCode.PaidInFull;
        if (!eligible) return;
        var order = await repository.GetOrder(invoiceEvent.Invoice.StoreId, orderId); if (order is null || order.Status == LicenseOrderStatus.Fulfilled) return;
        var product = await repository.GetProduct(order.StoreId, order.ProductId); if (product is null) return;
        var license = await Issue(order.StoreId, product, order.BuyerEmail, order.Id, order.InvoiceId);
        order.LicenseId = license.Id; order.Status = LicenseOrderStatus.Fulfilled; await repository.SaveOrder(order.StoreId, order);
        await SendEmail(order.StoreId, order.BuyerEmail, product, license, settings);
    }

    public async Task<ManagedLicense> Issue(string storeId, LicenseProduct product, string email, string? orderId = null, string? invoiceId = null)
    {
        string key; string hash; var attempts = 0;
        do { if (++attempts > 20) throw new InvalidOperationException("Could not generate a unique key."); key = generator.Generate(product.KeyPattern); hash = LicenseSecurityService.HashLicenseKey(key); } while (await repository.FindByKeyHash(storeId, hash) is not null);
        var license = new ManagedLicense { StoreId = storeId, ProductId = product.Id, OrderId = orderId, InvoiceId = invoiceId, CustomerEmail = email, KeyHash = hash, ProtectedKey = security.Protect(key), MaxActivations = product.MaxActivations, ExpiresAt = product.DurationDays is { } days ? DateTimeOffset.UtcNow.AddDays(days) : null };
        license.Audit.Add(new() { Action = "issued", Success = true, Detail = orderId is null ? "Manual issue" : "Paid order" });
        await repository.SaveLicense(storeId, license); return license;
    }

    private async Task SendEmail(string storeId, string email, LicenseProduct product, ManagedLicense license, LicenseManagerSettings settings)
    {
        if (!settings.EmailDeliveryEnabled || string.IsNullOrWhiteSpace(email)) return;
        var key = security.Unprotect(license.ProtectedKey); if (key is null) return;
        string E(string value) => HtmlEncoder.Default.Encode(value);
        var body = settings.EmailHtml.Replace("{ProductName}", E(product.Name), StringComparison.Ordinal).Replace("{LicenseKey}", E(key), StringComparison.Ordinal).Replace("{ExpiresAt}", E(license.ExpiresAt?.ToString("u") ?? "Never"), StringComparison.Ordinal);
        var subject = settings.EmailSubject.Replace("{ProductName}", product.Name, StringComparison.Ordinal);
        try { (await emailFactory.GetEmailSender(storeId)).SendEmail(MailboxAddress.Parse(email), subject, body); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not queue license email for {LicenseId}", license.Id); }
    }
}
