#nullable enable
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.MakePay.LicenseManager.Models;

public sealed class LicenseManagerSettings
{
    [Required, StringLength(80)] public string StorefrontTitle { get; set; } = "Software licenses";
    [StringLength(500)] public string StorefrontDescription { get; set; } = "Secure licenses delivered after payment.";
    [Required, StringLength(10)] public string Currency { get; set; } = "USD";
    [StringLength(500)] public string? LogoUrl { get; set; }
    [StringLength(500)] public string? FaviconUrl { get; set; }
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string AccentColor { get; set; } = "#7c3aed";
    public bool DeliverOnProcessing { get; set; }
    public bool RequireSignedRequests { get; set; } = true;
    [StringLength(80)] public string LicenseKeyHeader { get; set; } = "X-License-Key";
    [StringLength(80)] public string SignatureHeader { get; set; } = "X-License-Signature";
    [StringLength(80)] public string TimestampHeader { get; set; } = "X-License-Timestamp";
    [StringLength(80)] public string NonceHeader { get; set; } = "X-License-Nonce";
    [StringLength(80)] public string ResponseSignatureHeader { get; set; } = "X-License-Response-Signature";
    [Range(30, 900)] public int MaxClockSkewSeconds { get; set; } = 300;
    [Range(1, 600)] public int RequestsPerMinute { get; set; } = 60;
    public string? ProtectedApiSecret { get; set; }
    [StringLength(200)] public string EmailSubject { get; set; } = "Your {ProductName} license";
    public string EmailHtml { get; set; } = "<p>Your license for <strong>{ProductName}</strong>:</p><p style=\"font:700 20px monospace\">{LicenseKey}</p><p>Manage activations carefully. Expires: {ExpiresAt}</p>";
    public bool EmailDeliveryEnabled { get; set; } = true;
    public bool ShowMakePayPromotion { get; set; } = true;
    [StringLength(200)] public string PromotionText { get; set; } = "Created by MakePay.io — accept 90+ currencies in a decentralized way with BTCPay Server.";
}

public sealed class LicenseProduct
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Required, StringLength(80), RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")] public string Slug { get; set; } = "new-license";
    [Required, StringLength(160)] public string Name { get; set; } = "New license";
    [StringLength(4000)] public string Description { get; set; } = "";
    [Range(0.00000001, 1000000000)] public decimal Price { get; set; }
    [Range(0.00000001, 1000000000)] public decimal? CompareAtPrice { get; set; }
    [StringLength(80)] public string? Badge { get; set; }
    [Required, StringLength(200)] public string KeyPattern { get; set; } = "MP-{A:4}-{X:6}-{N:4}";
    [Range(1, 100)] public int MaxActivations { get; set; } = 1;
    [Range(1, 36500)] public int? DurationDays { get; set; } = 365;
    public bool Active { get; set; } = true;
    [Url, StringLength(500)] public string? ImageUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LicenseProductCollection { public List<LicenseProduct> Products { get; set; } = []; }
public enum ManagedLicenseStatus { Active, Suspended, Revoked, Expired }

public sealed class LicenseActivation
{
    public string InstanceHash { get; set; } = "";
    [StringLength(200)] public string? Label { get; set; }
    public DateTimeOffset ActivatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class LicenseAuditEvent
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
    public bool Success { get; set; }
    public string? InstanceHashPrefix { get; set; }
    public string? IpHashPrefix { get; set; }
    public string? Detail { get; set; }
}

public sealed class ManagedLicense
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StoreId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string? CheckoutId { get; set; }
    public string? OrderId { get; set; }
    public string? InvoiceId { get; set; }
    public string CustomerEmail { get; set; } = "";
    public string KeyHash { get; set; } = "";
    public string ProtectedKey { get; set; } = "";
    public ManagedLicenseStatus Status { get; set; } = ManagedLicenseStatus.Active;
    public int MaxActivations { get; set; } = 1;
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public List<LicenseActivation> Activations { get; set; } = [];
    public List<LicenseAuditEvent> Audit { get; set; } = [];
    [StringLength(2000)] public string? Notes { get; set; }
}

public sealed class ManagedLicenseCollection { public Dictionary<string, ManagedLicense> Licenses { get; set; } = new(StringComparer.OrdinalIgnoreCase); }
public enum LicenseOrderStatus { Pending, Fulfilled, Cancelled }
public sealed class LicenseOrder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StoreId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string? CheckoutId { get; set; }
    public string? InvoiceId { get; set; }
    public string? LicenseId { get; set; }
    public string BuyerEmail { get; set; } = "";
    public LicenseOrderStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class LicenseOrderCollection { public Dictionary<string, LicenseOrder> Orders { get; set; } = new(StringComparer.OrdinalIgnoreCase); }

public sealed class LicenseDashboardViewModel
{
    public required string StoreId { get; init; }
    public required LicenseManagerSettings Settings { get; init; }
    public required IReadOnlyList<LicenseProduct> Products { get; init; }
    public required IReadOnlyList<ManagedLicense> Licenses { get; init; }
    public required IReadOnlyList<LicenseOrder> Orders { get; init; }
}
public sealed class LicenseStorefrontViewModel { public required string StoreId { get; init; } public required LicenseManagerSettings Settings { get; init; } public required IReadOnlyList<LicenseProduct> Products { get; init; } }
public sealed class LicenseOrderViewModel { public required LicenseManagerSettings Settings { get; init; } public required LicenseProduct Product { get; init; } public required LicenseOrder Order { get; init; } public string? LicenseKey { get; init; } public DateTimeOffset? ExpiresAt { get; init; } }

public sealed class LicenseApiRequest
{
    [Required, StringLength(500)] public string InstanceId { get; set; } = "";
    [StringLength(200)] public string? InstanceLabel { get; set; }
}
public sealed record LicenseApiResponse(bool Valid, string Status, string? ProductId, DateTimeOffset? ExpiresAt, int Activations, int MaxActivations, string? Message);
