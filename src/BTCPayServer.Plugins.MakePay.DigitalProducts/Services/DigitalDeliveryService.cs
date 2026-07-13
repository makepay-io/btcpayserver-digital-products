#nullable enable
using System.Text.Encodings.Web;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

public sealed class DigitalDeliveryService(
    EventAggregator events,
    ILogger<DigitalDeliveryService> logger,
    DigitalDownloadsRepository repository,
    DownloadTokenService tokens,
    EmailSenderFactory emailSenderFactory,
    LinkGenerator links) : EventHostedServiceBase(events, logger)
{
    public const string TagPrefix = "MPDD#";
    public static string Tag(string orderId) => TagPrefix + orderId;

    protected override void SubscribeToEvents() => Subscribe<InvoiceEvent>();

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is not InvoiceEvent invoiceEvent) return;
        var orderId = invoiceEvent.Invoice.GetInternalTags(TagPrefix).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(orderId)) return;
        var settings = await repository.GetSettings(invoiceEvent.Invoice.StoreId);
        var eligible = invoiceEvent.EventCode is InvoiceEventCode.Completed or InvoiceEventCode.Confirmed or InvoiceEventCode.MarkedCompleted ||
                       settings.DeliverOnProcessing && invoiceEvent.EventCode == InvoiceEventCode.PaidInFull;
        if (!eligible) return;
        var order = await repository.GetOrder(invoiceEvent.Invoice.StoreId, orderId);
        if (order is null || order.Status is DigitalOrderStatus.Revoked or DigitalOrderStatus.Paid) return;
        var product = await repository.GetProduct(order.StoreId, order.ProductId);
        if (product is null) return;
        var created = tokens.Create();
        order.Status = DigitalOrderStatus.Paid;
        order.PaidAt = DateTimeOffset.UtcNow;
        order.ExpiresAt = DateTimeOffset.UtcNow.AddHours(product.LinkHours ?? settings.DefaultLinkHours);
        order.MaxDownloads = product.DownloadLimit ?? settings.DefaultDownloadLimit;
        order.TokenHash = created.Hash;
        order.ProtectedToken = created.ProtectedToken;
        await repository.SaveOrder(order.StoreId, order);
        await QueueEmail(order, product, settings, created.Token);
    }

    private async Task QueueEmail(DigitalOrder order, DigitalProduct product, DigitalDownloadsSettings settings, string token)
    {
        if (!settings.EmailDeliveryEnabled || string.IsNullOrWhiteSpace(order.BuyerEmail)) return;
        var path = links.GetPathByAction("Download", "DigitalDownloadsPublic", new { storeId = order.StoreId, orderId = order.Id, token })!;
        var downloadUrl = order.PublicBaseUrl.TrimEnd('/') + path;
        string Encode(string value) => HtmlEncoder.Default.Encode(value);
        var body = settings.EmailHtml
            .Replace("{StoreName}", Encode(settings.StorefrontTitle), StringComparison.Ordinal)
            .Replace("{ProductName}", Encode(product.Name), StringComparison.Ordinal)
            .Replace("{DownloadUrl}", Encode(downloadUrl), StringComparison.Ordinal)
            .Replace("{ExpiresAt}", Encode(order.ExpiresAt?.ToString("u") ?? ""), StringComparison.Ordinal);
        var subject = settings.EmailSubject
            .Replace("{StoreName}", settings.StorefrontTitle, StringComparison.Ordinal)
            .Replace("{ProductName}", product.Name, StringComparison.Ordinal);
        try
        {
            var sender = await emailSenderFactory.GetEmailSender(order.StoreId);
            sender.SendEmail(MailboxAddress.Parse(order.BuyerEmail), subject, body);
            order.DeliveryEmailQueued = true;
            await repository.SaveOrder(order.StoreId, order);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not queue digital delivery email for order {OrderId}", order.Id);
        }
    }
}
