#nullable enable
using System.Text.Encodings.Web;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;
using BTCPayServer.Plugins.MakePay.LicenseManager.Services;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

public sealed class DigitalCheckoutFulfillmentService(
    EventAggregator events,
    ILogger<DigitalCheckoutFulfillmentService> logger,
    DigitalDownloadsRepository downloads,
    LicenseRepository licenses,
    DownloadTokenService tokens,
    CustomerAccessService access,
    LicenseFulfillmentService licenseFulfillment,
    DigitalPublicUrlService publicUrls,
    EmailSenderFactory emailFactory) : EventHostedServiceBase(events, logger)
{
    public const string TagPrefix = "MPDP#";
    public static string Tag(string checkoutId) => TagPrefix + checkoutId;

    protected override void SubscribeToEvents() => Subscribe<InvoiceEvent>();

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is not InvoiceEvent invoiceEvent) return;
        var checkoutId = invoiceEvent.Invoice.GetInternalTags(TagPrefix).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(checkoutId)) return;
        if (invoiceEvent.EventCode is InvoiceEventCode.Expired or InvoiceEventCode.MarkedInvalid or InvoiceEventCode.FailedToConfirm)
        {
            await downloads.UpdateCheckout(invoiceEvent.Invoice.StoreId, checkoutId, checkout =>
            {
                if (checkout.Status == DigitalCheckoutStatus.Paid) return false;
                checkout.Status = DigitalCheckoutStatus.Cancelled;
                return true;
            });
            return;
        }

        var settings = await downloads.GetSettings(invoiceEvent.Invoice.StoreId);
        var eligible = invoiceEvent.EventCode is InvoiceEventCode.Completed or InvoiceEventCode.Confirmed or InvoiceEventCode.MarkedCompleted ||
                       settings.DeliverOnProcessing && invoiceEvent.EventCode == InvoiceEventCode.PaidInFull;
        if (!eligible) return;
        var checkout = await downloads.GetCheckout(invoiceEvent.Invoice.StoreId, checkoutId);
        if (checkout is null || checkout.Status == DigitalCheckoutStatus.Paid) return;
        if (checkout.Status == DigitalCheckoutStatus.Cancelled) return;

        var leaseCutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
        var acquired = await downloads.UpdateCheckout(checkout.StoreId, checkout.Id, current =>
        {
            if (current.Status != DigitalCheckoutStatus.Pending && !(current.Status == DigitalCheckoutStatus.Processing && (current.ProcessingStartedAt is null || current.ProcessingStartedAt < leaseCutoff))) return false;
            current.Status = DigitalCheckoutStatus.Processing;
            current.ProcessingStartedAt = DateTimeOffset.UtcNow;
            return true;
        });
        if (acquired is null) return;

        try
        {
            var digitalOrderIds = new List<string>();
            var licenseOrderIds = new List<string>();
            var licenseIds = new List<string>();
            for (var lineIndex = 0; lineIndex < checkout.Lines.Count; lineIndex++)
            {
                var line = checkout.Lines[lineIndex];
                for (var itemIndex = 0; itemIndex < line.Quantity; itemIndex++)
                {
                    if (line.Kind == DigitalProductKind.Download)
                    {
                        var product = line.DigitalProductSnapshot?.ToProduct() ?? await downloads.GetProduct(checkout.StoreId, line.ProductId);
                        if (product is null) continue;
                        var orderId = $"{checkout.Id}-d-{lineIndex}-{itemIndex}";
                        var order = await downloads.GetOrder(checkout.StoreId, orderId);
                        if (order is null)
                        {
                            var created = tokens.Create();
                            order = new DigitalOrder
                            {
                                Id = orderId,
                                StoreId = checkout.StoreId,
                                ProductId = product.Id,
                                CheckoutId = checkout.Id,
                                InvoiceId = checkout.InvoiceId,
                                PublicBaseUrl = checkout.PublicBaseUrl,
                                BuyerEmail = checkout.BuyerEmail,
                                Status = DigitalOrderStatus.Paid,
                                PaidAt = DateTimeOffset.UtcNow,
                                ExpiresAt = DateTimeOffset.UtcNow.AddHours(product.LinkHours ?? settings.DefaultLinkHours),
                                MaxDownloads = product.DownloadLimit ?? settings.DefaultDownloadLimit,
                                TokenHash = created.Hash,
                                ProtectedToken = created.ProtectedToken,
                                ProductSnapshot = line.DigitalProductSnapshot ?? DigitalProductSnapshot.From(product)
                            };
                            await downloads.SaveOrder(checkout.StoreId, order);
                        }
                        digitalOrderIds.Add(order.Id);
                    }
                    else
                    {
                        var product = await licenses.GetProduct(checkout.StoreId, line.ProductId);
                        if (product is null) continue;
                        var orderId = $"{checkout.Id}-l-{lineIndex}-{itemIndex}";
                        var order = await licenses.GetOrder(checkout.StoreId, orderId);
                        if (order is null)
                        {
                            order = new LicenseOrder
                            {
                                Id = orderId,
                                StoreId = checkout.StoreId,
                                ProductId = product.Id,
                                CheckoutId = checkout.Id,
                                BuyerEmail = checkout.BuyerEmail,
                                InvoiceId = checkout.InvoiceId
                            };
                        }
                        if (order.Status != LicenseOrderStatus.Fulfilled || string.IsNullOrWhiteSpace(order.LicenseId))
                        {
                            var license = await licenseFulfillment.Issue(checkout.StoreId, product, checkout.BuyerEmail, order.Id, checkout.InvoiceId);
                            order.LicenseId = license.Id;
                            order.Status = LicenseOrderStatus.Fulfilled;
                            await licenses.SaveOrder(checkout.StoreId, order);
                        }
                        licenseOrderIds.Add(order.Id);
                        if (order.LicenseId is not null) licenseIds.Add(order.LicenseId);
                    }
                }
            }

            checkout.DigitalOrderIds = digitalOrderIds;
            checkout.LicenseOrderIds = licenseOrderIds;
            checkout.LicenseIds = licenseIds;
            checkout.Status = DigitalCheckoutStatus.Paid;
            checkout.ProcessingStartedAt = null;
            checkout.PaidAt = DateTimeOffset.UtcNow;
            await downloads.SaveCheckout(checkout.StoreId, checkout);
            await SendEmail(checkout, settings);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Digital product checkout fulfillment failed for {CheckoutId}", checkout.Id);
            await downloads.UpdateCheckout(checkout.StoreId, checkout.Id, current =>
            {
                if (current.Status != DigitalCheckoutStatus.Processing) return false;
                current.Status = DigitalCheckoutStatus.Pending;
                current.ProcessingStartedAt = null;
                return true;
            });
        }
    }

    private async Task SendEmail(DigitalCheckout checkout, DigitalDownloadsSettings settings)
    {
        if (!settings.EmailDeliveryEnabled || string.IsNullOrWhiteSpace(checkout.BuyerEmail)) return;
        var accessToken = access.RecoverCheckoutAccess(checkout);
        if (accessToken is null) return;
        var legacyPrefix = DigitalPublicUrlService.LegacyPrefix(checkout.StoreId);
        var purchaseUrl = await publicUrls.Absolute(checkout.StoreId, checkout.PublicBaseUrl,
            $"{legacyPrefix}/purchase/{checkout.Id}?accessToken={Uri.EscapeDataString(accessToken)}");
        var libraryReturnPath = await publicUrls.CanonicalPath(
            checkout.StoreId, checkout.PublicBaseUrl, legacyPrefix + "/account");
        var libraryUrl = await publicUrls.Absolute(checkout.StoreId, checkout.PublicBaseUrl,
            $"{legacyPrefix}/login?returnUrl={Uri.EscapeDataString(libraryReturnPath)}");
        string E(string value) => HtmlEncoder.Default.Encode(value);
        var body = settings.PurchaseEmailHtml
            .Replace("{StoreName}", E(settings.StorefrontTitle), StringComparison.Ordinal)
            .Replace("{PurchaseUrl}", E(purchaseUrl), StringComparison.Ordinal)
            .Replace("{LibraryUrl}", E(libraryUrl), StringComparison.Ordinal);
        var subject = settings.PurchaseEmailSubject.Replace("{StoreName}", settings.StorefrontTitle, StringComparison.Ordinal);
        try
        {
            (await emailFactory.GetEmailSender(checkout.StoreId)).SendEmail(MailboxAddress.Parse(checkout.BuyerEmail), subject, body);
            checkout.DeliveryEmailQueued = true;
            await downloads.SaveCheckout(checkout.StoreId, checkout);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Could not queue digital purchase email for {CheckoutId}", checkout.Id); }
    }
}
