#nullable enable
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Models;

public enum ProductStorageKind
{
    Local,
    S3,
    CustomUrl
}

public sealed class DigitalDownloadsSettings
{
    [Required, StringLength(80)] public string StorefrontTitle { get; set; } = "Digital downloads";
    [StringLength(500)] public string StorefrontDescription { get; set; } = "Secure files, delivered after payment.";
    [Required, StringLength(10)] public string Currency { get; set; } = "USD";
    [StringLength(500)] public string? LogoUrl { get; set; }
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string AccentColor { get; set; } = "#0d6efd";
    public int DefaultDownloadLimit { get; set; } = 5;
    public int DefaultLinkHours { get; set; } = 72;
    public bool LockToFirstIp { get; set; }
    public bool DeliverOnProcessing { get; set; }
    public bool EmailDeliveryEnabled { get; set; } = true;
    [StringLength(200)] public string EmailSubject { get; set; } = "Your download from {StoreName}";
    public string EmailHtml { get; set; } = "<p>Thanks for your purchase of <strong>{ProductName}</strong>.</p><p><a href=\"{DownloadUrl}\">Download your file</a></p><p>This link expires {ExpiresAt}.</p>";
    [StringLength(500)] public string? TermsUrl { get; set; }
    [StringLength(200)] public string PromotionText { get; set; } = "Created by MakePay.io — accept 90+ currencies in a decentralized way with BTCPay Server.";
    public bool ShowMakePayPromotion { get; set; } = true;
    [StringLength(200)] public string S3Endpoint { get; set; } = "https://s3.amazonaws.com";
    [StringLength(100)] public string S3Region { get; set; } = "us-east-1";
    [StringLength(200)] public string? S3Bucket { get; set; }
    [StringLength(200)] public string? S3AccessKey { get; set; }
    public string? ProtectedS3SecretKey { get; set; }
    [StringLength(200)] public string? RemoteAuthorizationHeader { get; set; }
    public string? ProtectedRemoteAuthorizationValue { get; set; }
}

public sealed class DigitalProduct
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required, StringLength(80), RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")] public string Slug { get; set; } = "new-product";
    [Required, StringLength(160)] public string Name { get; set; } = "New product";
    [StringLength(4000)] public string Description { get; set; } = "";
    [Range(0.00000001, 1000000000)] public decimal Price { get; set; }
    [StringLength(500)] public string? ImageUrl { get; set; }
    public bool Active { get; set; } = true;
    public ProductStorageKind StorageKind { get; set; }
    [StringLength(1000)] public string? StorageLocation { get; set; }
    [StringLength(260)] public string DownloadFileName { get; set; } = "download.bin";
    [StringLength(200)] public string ContentType { get; set; } = "application/octet-stream";
    public long? FileSize { get; set; }
    [Range(0, 10000)] public int? DownloadLimit { get; set; }
    [Range(1, 8760)] public int? LinkHours { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DigitalCatalog
{
    public List<DigitalProduct> Products { get; set; } = [];
}

public enum DigitalOrderStatus
{
    Pending,
    Paid,
    Expired,
    Revoked
}

public sealed class DigitalOrder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StoreId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string? InvoiceId { get; set; }
    public string PublicBaseUrl { get; set; } = "";
    public string BuyerEmail { get; set; } = "";
    public DigitalOrderStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int DownloadCount { get; set; }
    public int MaxDownloads { get; set; }
    public string? TokenHash { get; set; }
    public string? ProtectedToken { get; set; }
    public string? FirstIpHash { get; set; }
    public DateTimeOffset? LastDownloadAt { get; set; }
    public bool DeliveryEmailQueued { get; set; }
}

public sealed class DigitalOrderCollection
{
    public Dictionary<string, DigitalOrder> Orders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DigitalDownloadsDashboardViewModel
{
    public required string StoreId { get; init; }
    public required DigitalDownloadsSettings Settings { get; init; }
    public required IReadOnlyList<DigitalProduct> Products { get; init; }
    public required IReadOnlyList<DigitalOrder> Orders { get; init; }
}

public sealed class StorefrontViewModel
{
    public required string StoreId { get; init; }
    public required DigitalDownloadsSettings Settings { get; init; }
    public required IReadOnlyList<DigitalProduct> Products { get; init; }
    public required LicenseManagerSettings LicenseSettings { get; init; }
    public required IReadOnlyList<LicenseProduct> LicenseProducts { get; init; }
}

public sealed class OrderViewModel
{
    public required DigitalDownloadsSettings Settings { get; init; }
    public required DigitalProduct Product { get; init; }
    public required DigitalOrder Order { get; init; }
    public string? DownloadUrl { get; init; }
}
