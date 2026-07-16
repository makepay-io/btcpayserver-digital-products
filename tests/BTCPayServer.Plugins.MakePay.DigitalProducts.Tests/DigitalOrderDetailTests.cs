using System.Reflection;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Controllers;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class DigitalOrderDetailTests
{
    [Fact]
    public void Checkout_detail_combines_snapshotted_lines_download_usage_and_license_fulfillment()
    {
        var createdAt = DateTimeOffset.Parse("2026-07-16T08:00:00Z");
        var digitalOrder = new DigitalOrder
        {
            Id = "checkout-1-d-0-0",
            StoreId = "store-1",
            CheckoutId = "checkout-1",
            InvoiceId = "invoice-1",
            ProductId = "deleted-guide",
            BuyerEmail = "buyer@example.com",
            Status = DigitalOrderStatus.Paid,
            CreatedAt = createdAt,
            PaidAt = createdAt.AddMinutes(2),
            ExpiresAt = createdAt.AddDays(3),
            DownloadCount = 2,
            MaxDownloads = 5,
            LastDownloadAt = createdAt.AddHours(1),
            TokenHash = "must-not-project",
            ProtectedToken = "must-not-project",
            FirstIpHash = "must-not-project",
            ProductSnapshot = new DigitalProductSnapshot
            {
                Id = "deleted-guide",
                Name = "Archived Commerce Guide",
                ProductType = DigitalProductType.PdfEbook,
                DeliveryMode = DigitalDeliveryMode.StreamAndDownload,
                DownloadFileName = "guide.pdf",
                FileSize = 2048
            }
        };
        var licenseOrder = new LicenseOrder
        {
            Id = "checkout-1-l-1-0",
            StoreId = "store-1",
            CheckoutId = "checkout-1",
            InvoiceId = "invoice-1",
            ProductId = "pro-license",
            LicenseId = "license-1",
            BuyerEmail = "buyer@example.com",
            Status = LicenseOrderStatus.Fulfilled,
            CreatedAt = createdAt
        };
        var checkout = new DigitalCheckout
        {
            Id = "checkout-1",
            StoreId = "store-1",
            BuyerEmail = "buyer@example.com",
            InvoiceId = "invoice-1",
            Currency = "USD",
            Total = 59m,
            Status = DigitalCheckoutStatus.Paid,
            CreatedAt = createdAt,
            PaidAt = createdAt.AddMinutes(2),
            DigitalOrderIds = [digitalOrder.Id],
            LicenseOrderIds = [licenseOrder.Id],
            Lines =
            [
                new DigitalCheckoutLine
                {
                    Kind = DigitalProductKind.Download,
                    ProductId = "deleted-guide",
                    Name = "Archived Commerce Guide",
                    Quantity = 1,
                    UnitPrice = 10m,
                    DigitalProductSnapshot = digitalOrder.ProductSnapshot
                },
                new DigitalCheckoutLine
                {
                    Kind = DigitalProductKind.License,
                    ProductId = "pro-license",
                    Name = "Demo Pro License",
                    Quantity = 1,
                    UnitPrice = 49m
                }
            ]
        };
        var managedLicense = new ManagedLicense
        {
            Id = "license-1",
            StoreId = "store-1",
            OrderId = licenseOrder.Id,
            CheckoutId = checkout.Id,
            ProductId = "pro-license",
            ProtectedKey = "must-not-project",
            KeyHash = "must-not-project",
            CustomerEmail = "buyer@example.com",
            Status = ManagedLicenseStatus.Active,
            MaxActivations = 2,
            IssuedAt = createdAt.AddMinutes(2),
            Activations = [new LicenseActivation { InstanceHash = "must-not-project" }]
        };

        var result = DigitalOrderDetailBuilder.Build(
            "store-1",
            digitalOrder.Id,
            digitalOrder,
            null,
            checkout,
            [digitalOrder],
            [],
            [licenseOrder],
            [new LicenseProduct { Id = "pro-license", Name = "Demo Pro License", MaxActivations = 2 }],
            [managedLicense],
            orderSearch: " buyer ",
            orderStatus: "paid",
            orderPage: 2,
            orderPageSize: 10);

        Assert.Equal("buyer@example.com", result.BuyerEmail);
        Assert.Equal("invoice-1", result.InvoiceId);
        Assert.Equal(59m, result.Total);
        Assert.Equal("USD", result.Currency);
        Assert.Equal(DigitalCheckoutStatus.Paid, result.CheckoutStatus);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(59m, result.Lines.Sum(line => line.Total));
        var delivery = Assert.Single(result.DigitalDeliveries);
        Assert.Equal("Archived Commerce Guide", delivery.ProductName);
        Assert.Equal(DigitalProductType.PdfEbook, delivery.ProductType);
        Assert.Equal(2, delivery.DownloadCount);
        Assert.True(delivery.IsIpLocked);
        var license = Assert.Single(result.LicenseDeliveries);
        Assert.Equal(ManagedLicenseStatus.Active, license.LicenseStatus);
        Assert.Equal(1, license.Activations);
        Assert.Equal(2, license.MaxActivations);
        Assert.Equal("buyer", result.OrderSearch);
        Assert.Equal("Paid", result.OrderStatus);
        Assert.Equal(2, result.OrderPage);
    }

    [Fact]
    public void Legacy_order_without_checkout_uses_product_snapshot_and_does_not_invent_historical_price()
    {
        var order = new DigitalOrder
        {
            Id = "legacy-order",
            ProductId = "deleted-product",
            BuyerEmail = "legacy@example.com",
            Status = DigitalOrderStatus.Revoked,
            ProductSnapshot = new DigitalProductSnapshot
            {
                Id = "deleted-product",
                Name = "Historical Product",
                ProductType = DigitalProductType.Audio,
                DeliveryMode = DigitalDeliveryMode.Stream,
                DownloadFileName = "track.flac"
            }
        };

        var result = DigitalOrderDetailBuilder.Build(
            "store-1", order.Id, order, null, null, [order], [], [], [], []);

        Assert.Null(result.Total);
        Assert.Null(result.CheckoutStatus);
        Assert.Equal("Revoked", result.StatusLabel);
        var line = Assert.Single(result.Lines);
        Assert.Equal("Historical Product", line.Name);
        Assert.Null(line.UnitPrice);
        Assert.Null(line.Total);
    }

    [Fact]
    public void Checkout_linked_managed_license_is_visible_even_when_legacy_license_order_is_missing()
    {
        var checkout = new DigitalCheckout
        {
            Id = "checkout-1",
            BuyerEmail = "buyer@example.com",
            Currency = "USD",
            Status = DigitalCheckoutStatus.Paid,
            LicenseIds = ["license-1"],
            Lines =
            [
                new DigitalCheckoutLine
                {
                    Kind = DigitalProductKind.License,
                    ProductId = "license-product",
                    Name = "Legacy License",
                    Quantity = 1,
                    UnitPrice = 15m
                }
            ]
        };
        var license = new ManagedLicense
        {
            Id = "license-1",
            CheckoutId = checkout.Id,
            ProductId = "license-product",
            CustomerEmail = "buyer@example.com",
            ProtectedKey = "must-not-project",
            KeyHash = "must-not-project",
            Status = ManagedLicenseStatus.Active,
            MaxActivations = 3
        };

        var result = DigitalOrderDetailBuilder.Build(
            "store-1", checkout.Id, null, null, checkout, [], [], [], [], [license]);

        var projected = Assert.Single(result.LicenseDeliveries);
        Assert.Equal("Legacy License", projected.ProductName);
        Assert.Equal("license-1", projected.LicenseId);
        Assert.Null(projected.OrderStatus);
        Assert.Equal(ManagedLicenseStatus.Active, projected.LicenseStatus);
    }

    [Fact]
    public void License_fulfillment_prefers_exact_license_deduplicates_recovered_licenses_and_keeps_checkout_name()
    {
        var checkout = new DigitalCheckout
        {
            Id = "checkout-1",
            BuyerEmail = "buyer@example.com",
            Currency = "USD",
            Status = DigitalCheckoutStatus.Paid,
            Lines =
            [
                new DigitalCheckoutLine
                {
                    Kind = DigitalProductKind.License,
                    ProductId = "license-product",
                    Name = "Purchased License Name",
                    Quantity = 2,
                    UnitPrice = 20m
                }
            ]
        };
        var exactOrder = new LicenseOrder
        {
            Id = "order-exact",
            CheckoutId = checkout.Id,
            ProductId = "license-product",
            LicenseId = "license-exact",
            Status = LicenseOrderStatus.Fulfilled
        };
        var legacyOrder = new LicenseOrder
        {
            Id = "order-legacy",
            CheckoutId = checkout.Id,
            ProductId = "license-product",
            LicenseId = null,
            Status = LicenseOrderStatus.Fulfilled
        };
        checkout.LicenseOrderIds = [exactOrder.Id, legacyOrder.Id];
        var reissuedForExactOrder = new ManagedLicense
        {
            Id = "license-reissued",
            OrderId = exactOrder.Id,
            CheckoutId = checkout.Id,
            ProductId = "license-product",
            Status = ManagedLicenseStatus.Revoked,
            MaxActivations = 9
        };
        var exactLicense = new ManagedLicense
        {
            Id = "license-exact",
            OrderId = exactOrder.Id,
            CheckoutId = checkout.Id,
            ProductId = "license-product",
            Status = ManagedLicenseStatus.Active,
            MaxActivations = 2
        };
        var recoveredLegacyLicense = new ManagedLicense
        {
            Id = "license-legacy",
            OrderId = legacyOrder.Id,
            CheckoutId = checkout.Id,
            ProductId = "license-product",
            Status = ManagedLicenseStatus.Active,
            MaxActivations = 3
        };

        var result = DigitalOrderDetailBuilder.Build(
            "store-1",
            checkout.Id,
            null,
            null,
            checkout,
            [],
            [],
            [exactOrder, legacyOrder],
            [new LicenseProduct { Id = "license-product", Name = "Renamed Catalog Product" }],
            [reissuedForExactOrder, exactLicense, recoveredLegacyLicense]);

        Assert.Equal(3, result.LicenseDeliveries.Count);
        var exact = Assert.Single(result.LicenseDeliveries, delivery => delivery.LicenseId == "license-exact");
        Assert.Equal("license-exact", exact.LicenseId);
        Assert.Equal(ManagedLicenseStatus.Active, exact.LicenseStatus);
        Assert.Equal("Purchased License Name", exact.ProductName);
        var legacy = Assert.Single(result.LicenseDeliveries, delivery => delivery.OrderId == legacyOrder.Id);
        Assert.Equal("license-legacy", legacy.LicenseId);
        Assert.Equal("Purchased License Name", legacy.ProductName);
        Assert.Single(result.LicenseDeliveries, delivery => delivery.LicenseId == "license-legacy");
        Assert.Single(result.LicenseDeliveries, delivery => delivery.LicenseId == "license-reissued");
    }

    [Fact]
    public void Admin_projection_has_no_secret_bearing_properties()
    {
        var forbidden = new[]
        {
            "TokenHash", "ProtectedToken", "FirstIpHash", "StorageLocation", "ProtectedKey", "KeyHash", "InstanceHash"
        };
        var projectionTypes = new[]
        {
            typeof(DigitalAdminOrderDetailViewModel),
            typeof(DigitalAdminOrderLineViewModel),
            typeof(DigitalAdminDeliveryViewModel),
            typeof(DigitalAdminLicenseDeliveryViewModel)
        };

        foreach (var type in projectionTypes)
            foreach (var property in forbidden)
                Assert.Null(type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
    }

    [Fact]
    public void Admin_route_dashboard_links_and_detail_actions_are_wired_without_rendering_secrets()
    {
        var method = typeof(DigitalDownloadsAdminController).GetMethod(nameof(DigitalDownloadsAdminController.OrderDetail))!;
        var route = Assert.Single(method.GetCustomAttributes<HttpGetAttribute>());
        Assert.Equal("orders/{orderId}", route.Template);

        var index = Source("Views", "DigitalDownloads", "Index.cshtml");
        var view = Source("Views", "DigitalDownloads", "OrderDetail.cshtml");
        var controller = Source("Controllers", "DigitalDownloadsAdminController.cs");

        Assert.Contains("asp-action=\"OrderDetail\"", index, StringComparison.Ordinal);
        Assert.Contains("View order", index, StringComparison.Ordinal);
        Assert.Contains("Purchased items", view, StringComparison.Ordinal);
        Assert.Contains("Protected delivery", view, StringComparison.Ordinal);
        Assert.Contains("License fulfillment", view, StringComparison.Ordinal);
        Assert.Contains("asp-controller=\"UIInvoice\"", view, StringComparison.Ordinal);
        Assert.Contains("name=\"returnToDetail\"", view, StringComparison.Ordinal);
        Assert.Contains("detailOrderId", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ProtectedToken", view, StringComparison.Ordinal);
        Assert.DoesNotContain("TokenHash", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ProtectedKey", view, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstIpHash", view, StringComparison.Ordinal);
        Assert.DoesNotContain("StorageLocation", view, StringComparison.Ordinal);
    }

    private static string Source(params string[] segments) =>
        File.ReadAllText(CustomDomainGuideTests.RepositoryFile(["src", "BTCPayServer.Plugins.MakePay.DigitalProducts", .. segments]));
}
