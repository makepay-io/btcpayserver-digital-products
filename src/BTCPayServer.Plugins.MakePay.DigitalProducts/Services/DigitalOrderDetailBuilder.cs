#nullable enable
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

internal static class DigitalOrderDetailBuilder
{
    internal static DigitalAdminOrderDetailViewModel Build(
        string storeId,
        string requestedOrderId,
        DigitalOrder? selectedDigitalOrder,
        LicenseOrder? selectedLicenseOrder,
        DigitalCheckout? checkout,
        IEnumerable<DigitalOrder> digitalOrders,
        IEnumerable<DigitalProduct> digitalProducts,
        IEnumerable<LicenseOrder> licenseOrders,
        IEnumerable<LicenseProduct> licenseProducts,
        IEnumerable<ManagedLicense> managedLicenses,
        string? returnSection = null,
        string? orderSearch = null,
        string? orderProductId = null,
        string? orderStatus = null,
        int orderPage = 1,
        int orderPageSize = 25)
    {
        var productsById = digitalProducts
            .GroupBy(product => product.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var licenseProductsById = licenseProducts
            .GroupBy(product => product.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var allDigitalOrders = digitalOrders.ToList();
        var allLicenseOrders = licenseOrders.ToList();
        var allLicenses = managedLicenses.ToList();

        var relatedDigitalOrders = RelatedDigitalOrders(checkout, selectedDigitalOrder, allDigitalOrders);
        var relatedLicenseOrders = RelatedLicenseOrders(checkout, selectedLicenseOrder, allLicenseOrders);
        var checkoutLines = checkout?.Lines ?? [];
        var lines = checkoutLines.Count > 0
            ? checkoutLines.Select(line => Line(line, productsById, licenseProductsById)).ToList()
            : FallbackLines(relatedDigitalOrders, relatedLicenseOrders, productsById, licenseProductsById);

        var digitalDeliveries = relatedDigitalOrders.Select(order =>
        {
            productsById.TryGetValue(order.ProductId, out var currentProduct);
            var product = order.ProductSnapshot?.ToProduct() ?? currentProduct ?? new DigitalProduct
            {
                Id = order.ProductId,
                Name = CheckoutName(checkoutLines, DigitalProductKind.Download, order.ProductId) ?? "Deleted product"
            };
            return new DigitalAdminDeliveryViewModel
            {
                OrderId = order.Id,
                ProductId = order.ProductId,
                ProductName = order.ProductSnapshot?.Name ??
                              CheckoutName(checkoutLines, DigitalProductKind.Download, order.ProductId) ??
                              currentProduct?.Name ?? "Deleted product",
                ProductType = product.ProductType,
                DeliveryMode = product.DeliveryMode,
                DownloadFileName = product.DownloadFileName,
                FileSize = product.FileSize,
                Status = order.Status,
                CreatedAt = order.CreatedAt,
                PaidAt = order.PaidAt,
                ExpiresAt = order.ExpiresAt,
                DownloadCount = order.DownloadCount,
                MaxDownloads = order.MaxDownloads,
                LastDownloadAt = order.LastDownloadAt,
                LastStreamAt = order.LastStreamAt,
                IsIpLocked = !string.IsNullOrWhiteSpace(order.FirstIpHash),
                DeliveryEmailQueued = order.DeliveryEmailQueued
            };
        }).ToList();

        var licenseDeliveries = relatedLicenseOrders.Select(order =>
        {
            licenseProductsById.TryGetValue(order.ProductId, out var product);
            var license = !string.IsNullOrWhiteSpace(order.LicenseId)
                ? allLicenses.FirstOrDefault(item => item.Id.Equals(order.LicenseId, StringComparison.OrdinalIgnoreCase))
                : null;
            license ??= allLicenses.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.OrderId) &&
                item.OrderId.Equals(order.Id, StringComparison.OrdinalIgnoreCase));
            return new DigitalAdminLicenseDeliveryViewModel
            {
                OrderId = order.Id,
                ProductId = order.ProductId,
                ProductName = CheckoutName(checkoutLines, DigitalProductKind.License, order.ProductId) ??
                              product?.Name ?? "Deleted license product",
                LicenseId = license?.Id ?? order.LicenseId,
                OrderStatus = order.Status,
                LicenseStatus = license?.Status,
                CreatedAt = order.CreatedAt,
                IssuedAt = license?.IssuedAt,
                ExpiresAt = license?.ExpiresAt,
                Activations = license?.Activations.Count ?? 0,
                MaxActivations = license?.MaxActivations ?? product?.MaxActivations ?? 0
            };
        }).ToList();
        if (checkout is not null)
        {
            var representedLicenseIds = licenseDeliveries
                .Where(delivery => !string.IsNullOrWhiteSpace(delivery.LicenseId))
                .Select(delivery => delivery.LicenseId!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var license in allLicenses.Where(license =>
                         !representedLicenseIds.Contains(license.Id) &&
                         (license.CheckoutId?.Equals(checkout.Id, StringComparison.OrdinalIgnoreCase) is true ||
                          checkout.LicenseIds.Contains(license.Id, StringComparer.OrdinalIgnoreCase))))
            {
                licenseProductsById.TryGetValue(license.ProductId, out var product);
                licenseDeliveries.Add(new DigitalAdminLicenseDeliveryViewModel
                {
                    OrderId = license.OrderId ?? license.Id,
                    ProductId = license.ProductId,
                    ProductName = CheckoutName(checkoutLines, DigitalProductKind.License, license.ProductId) ??
                                  product?.Name ?? "Deleted license product",
                    LicenseId = license.Id,
                    OrderStatus = null,
                    LicenseStatus = license.Status,
                    CreatedAt = license.IssuedAt,
                    IssuedAt = license.IssuedAt,
                    ExpiresAt = license.ExpiresAt,
                    Activations = license.Activations.Count,
                    MaxActivations = license.MaxActivations
                });
            }
        }

        var buyerEmail = checkout?.BuyerEmail ?? selectedDigitalOrder?.BuyerEmail ?? selectedLicenseOrder?.BuyerEmail ?? "";
        var createdAt = checkout?.CreatedAt ?? selectedDigitalOrder?.CreatedAt ?? selectedLicenseOrder?.CreatedAt ?? DateTimeOffset.UtcNow;
        var statusLabel = StatusLabel(checkout, selectedDigitalOrder, selectedLicenseOrder);

        return new DigitalAdminOrderDetailViewModel
        {
            StoreId = storeId,
            OrderId = requestedOrderId,
            BuyerEmail = buyerEmail,
            StatusLabel = statusLabel,
            CheckoutId = checkout?.Id ?? selectedDigitalOrder?.CheckoutId ?? selectedLicenseOrder?.CheckoutId,
            InvoiceId = checkout?.InvoiceId ?? selectedDigitalOrder?.InvoiceId ?? selectedLicenseOrder?.InvoiceId,
            CheckoutStatus = checkout?.Status,
            Total = checkout?.Total,
            Currency = checkout?.Currency ?? "",
            CreatedAt = createdAt,
            PaidAt = checkout?.PaidAt ?? selectedDigitalOrder?.PaidAt,
            ReservationExpiresAt = checkout?.ReservationExpiresAt,
            DeliveryEmailQueued = checkout?.DeliveryEmailQueued == true || digitalDeliveries.Any(delivery => delivery.DeliveryEmailQueued),
            Lines = lines,
            DigitalDeliveries = digitalDeliveries,
            LicenseDeliveries = licenseDeliveries,
            ReturnSection = string.Equals(returnSection, "licenses", StringComparison.OrdinalIgnoreCase) ? "licenses" : "products",
            OrderSearch = Normalize(orderSearch, 200),
            OrderProductId = Normalize(orderProductId, 120),
            OrderStatus = NormalizeStatus(orderStatus),
            OrderPage = Math.Max(1, orderPage),
            OrderPageSize = orderPageSize is 10 or 25 or 50 or 100 ? orderPageSize : 25
        };
    }

    private static List<DigitalOrder> RelatedDigitalOrders(
        DigitalCheckout? checkout,
        DigitalOrder? selected,
        IReadOnlyList<DigitalOrder> allOrders)
    {
        List<DigitalOrder> related = checkout is null
            ? []
            : allOrders.Where(order =>
                    order.CheckoutId?.Equals(checkout.Id, StringComparison.OrdinalIgnoreCase) is true ||
                    checkout.DigitalOrderIds.Contains(order.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();
        if (selected is not null && related.All(order => !order.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase)))
            related.Add(selected);
        return related.OrderBy(order => order.CreatedAt).ThenBy(order => order.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<LicenseOrder> RelatedLicenseOrders(
        DigitalCheckout? checkout,
        LicenseOrder? selected,
        IReadOnlyList<LicenseOrder> allOrders)
    {
        List<LicenseOrder> related = checkout is null
            ? []
            : allOrders.Where(order =>
                    order.CheckoutId?.Equals(checkout.Id, StringComparison.OrdinalIgnoreCase) is true ||
                    checkout.LicenseOrderIds.Contains(order.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();
        if (selected is not null && related.All(order => !order.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase)))
            related.Add(selected);
        return related.OrderBy(order => order.CreatedAt).ThenBy(order => order.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static DigitalAdminOrderLineViewModel Line(
        DigitalCheckoutLine line,
        IReadOnlyDictionary<string, DigitalProduct> digitalProducts,
        IReadOnlyDictionary<string, LicenseProduct> licenseProducts)
    {
        digitalProducts.TryGetValue(line.ProductId, out var digitalProduct);
        licenseProducts.TryGetValue(line.ProductId, out var licenseProduct);
        var snapshot = line.DigitalProductSnapshot;
        return new DigitalAdminOrderLineViewModel
        {
            ProductId = line.ProductId,
            Name = snapshot?.Name ??
                   (!string.IsNullOrWhiteSpace(line.Name) ? line.Name : null) ??
                   digitalProduct?.Name ?? licenseProduct?.Name ?? "Deleted product",
            Kind = line.Kind,
            ProductType = line.Kind == DigitalProductKind.Download ? snapshot?.ProductType ?? digitalProduct?.ProductType : null,
            DeliveryMode = line.Kind == DigitalProductKind.Download ? snapshot?.DeliveryMode ?? digitalProduct?.DeliveryMode : null,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            Total = line.Total
        };
    }

    private static IReadOnlyList<DigitalAdminOrderLineViewModel> FallbackLines(
        IReadOnlyList<DigitalOrder> digitalOrders,
        IReadOnlyList<LicenseOrder> licenseOrders,
        IReadOnlyDictionary<string, DigitalProduct> digitalProducts,
        IReadOnlyDictionary<string, LicenseProduct> licenseProducts)
    {
        var lines = new List<DigitalAdminOrderLineViewModel>();
        lines.AddRange(digitalOrders.Select(order =>
        {
            digitalProducts.TryGetValue(order.ProductId, out var product);
            return new DigitalAdminOrderLineViewModel
            {
                ProductId = order.ProductId,
                Name = order.ProductSnapshot?.Name ?? product?.Name ?? "Deleted product",
                Kind = DigitalProductKind.Download,
                ProductType = order.ProductSnapshot?.ProductType ?? product?.ProductType,
                DeliveryMode = order.ProductSnapshot?.DeliveryMode ?? product?.DeliveryMode,
                Quantity = 1
            };
        }));
        lines.AddRange(licenseOrders.Select(order =>
        {
            licenseProducts.TryGetValue(order.ProductId, out var product);
            return new DigitalAdminOrderLineViewModel
            {
                ProductId = order.ProductId,
                Name = product?.Name ?? "Deleted license product",
                Kind = DigitalProductKind.License,
                Quantity = 1
            };
        }));
        return lines;
    }

    private static string? CheckoutName(IEnumerable<DigitalCheckoutLine> lines, DigitalProductKind kind, string productId) =>
        lines.FirstOrDefault(line => line.Kind == kind && line.ProductId.Equals(productId, StringComparison.OrdinalIgnoreCase))?.Name;

    private static string StatusLabel(DigitalCheckout? checkout, DigitalOrder? digital, LicenseOrder? license)
    {
        if (checkout is null) return digital?.Status.ToString() ?? license?.Status.ToString() ?? "Unknown";
        if (digital is not null && digital.Status != DigitalOrderStatus.Paid)
            return $"{checkout.Status} · {digital.Status} access";
        if (license is not null && license.Status != LicenseOrderStatus.Fulfilled)
            return $"{checkout.Status} · {license.Status} license";
        return checkout.Status.ToString();
    }

    private static string Normalize(string? value, int maximumLength)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static string NormalizeStatus(string? value) =>
        Enum.TryParse<DigitalOrderStatus>(value, true, out var status) && Enum.IsDefined(status) ? status.ToString() : "";
}
