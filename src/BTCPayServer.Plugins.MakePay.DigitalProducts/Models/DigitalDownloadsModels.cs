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

public enum DigitalStoreFontStyle
{
    [Display(Name = "System sans")] System,
    [Display(Name = "Modern sans")] Modern,
    [Display(Name = "Rounded sans")] Rounded,
    [Display(Name = "Editorial serif")] Editorial,
    [Display(Name = "Neo-grotesk sans")] Grotesk
}

public enum DigitalProductKind { Download, License }

public sealed class DigitalDownloadsSettings
{
    [Required, StringLength(80)] public string StorefrontTitle { get; set; } = "Digital downloads";
    [StringLength(500)] public string StorefrontDescription { get; set; } = "Secure files, delivered after payment.";
    [Required, StringLength(10)] public string Currency { get; set; } = "USD";
    [Url, StringLength(500)] public string? LogoUrl { get; set; }
    [Url, StringLength(500)] public string? HeroImageUrl { get; set; }
    [StringLength(80)] public string HeroEyebrow { get; set; } = "MakePay Digital";
    [StringLength(180)] public string HeroHeadline { get; set; } = "Digital goods, delivered directly";
    [StringLength(320)] public string HeroSubheadline { get; set; } = "Secure downloads and software licenses, paid directly with BTCPay Server.";
    [StringLength(120)] public string CatalogTitle { get; set; } = "Explore digital products";
    [StringLength(280)] public string CatalogSubtitle { get; set; } = "Add products to your cart and access every purchase from one private library.";
    [StringLength(120)] public string LibraryTitle { get; set; } = "Your purchases";
    [StringLength(300)] public string LibrarySubtitle { get; set; } = "Downloads, license keys, receipts, and order status in one place.";
    [StringLength(120)] public string ConfirmationTitle { get; set; } = "Your purchase is ready";
    [StringLength(500)] public string ConfirmationMessage { get; set; } = "Payment is confirmed. Your files and license keys are now available.";
    [StringLength(1000)] public string FooterText { get; set; } = "Payments and delivery are processed securely by this BTCPay Server.";
    [Url, StringLength(500)] public string? PrivacyUrl { get; set; }
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string AccentColor { get; set; } = "#155EEF";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string AccentTextColor { get; set; } = "#FFFFFF";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string BrandTextColor { get; set; } = "#FFFFFF";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string PageBackgroundColor { get; set; } = "#FFFFFF";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string SurfaceColor { get; set; } = "#FFFFFF";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string TextColor { get; set; } = "#101828";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string MutedColor { get; set; } = "#667085";
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string SoftColor { get; set; } = "#F8FAFC";
    public DigitalStoreFontStyle FontStyle { get; set; } = DigitalStoreFontStyle.Modern;
    [Range(35, 60)] public int BrandPanelWidth { get; set; } = 46;
    public bool CustomerAccountsEnabled { get; set; } = true;
    [Range(5, 30)] public int LoginCodeMinutes { get; set; } = 10;
    [Range(1, 720)] public int CustomerSessionHours { get; set; } = 168;
    [StringLength(200)] public string LoginEmailSubject { get; set; } = "Your {StoreName} sign-in code";
    public string LoginEmailHtml { get; set; } = "<p>Your sign-in code for <strong>{StoreName}</strong> is:</p><p style=\"font:700 28px monospace;letter-spacing:.16em\">{Code}</p><p>This code expires in {Minutes} minutes.</p>";
    [StringLength(200)] public string PurchaseEmailSubject { get; set; } = "Your purchase from {StoreName}";
    public string PurchaseEmailHtml { get; set; } = "<p>Thanks for your purchase from <strong>{StoreName}</strong>.</p><p><a href=\"{PurchaseUrl}\">Open your protected purchase</a></p><p><a href=\"{LibraryUrl}\">View your purchase library</a></p>";
    public int DefaultDownloadLimit { get; set; } = 5;
    public int DefaultLinkHours { get; set; } = 72;
    public bool LockToFirstIp { get; set; }
    public bool DeliverOnProcessing { get; set; }
    public bool EmailDeliveryEnabled { get; set; } = true;
    [StringLength(200)] public string EmailSubject { get; set; } = "Your download from {StoreName}";
    public string EmailHtml { get; set; } = "<p>Thanks for your purchase of <strong>{ProductName}</strong>.</p><p><a href=\"{DownloadUrl}\">Download your file</a></p><p>This link expires {ExpiresAt}.</p>";
    [Url, StringLength(500)] public string? TermsUrl { get; set; }
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
    [Range(0.00000001, 1000000000)] public decimal? CompareAtPrice { get; set; }
    [StringLength(80)] public string? Badge { get; set; }
    [Url, StringLength(500)] public string? ImageUrl { get; set; }
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
    public string? CheckoutId { get; set; }
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

public sealed class DigitalCartLine
{
    public DigitalProductKind Kind { get; set; }
    public string ProductId { get; set; } = "";
    [Range(1, 10)] public int Quantity { get; set; } = 1;
}

public sealed class DigitalCartState
{
    public List<DigitalCartLine> Lines { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DigitalCheckoutLine
{
    public DigitalProductKind Kind { get; set; }
    public string ProductId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total => UnitPrice * Quantity;
}

public enum DigitalCheckoutStatus { Pending, Processing, Paid, Cancelled }

public sealed class DigitalCheckout
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StoreId { get; set; } = "";
    public string BuyerEmail { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public List<DigitalCheckoutLine> Lines { get; set; } = [];
    public decimal Total { get; set; }
    public string? InvoiceId { get; set; }
    public string PublicBaseUrl { get; set; } = "";
    public string? PublicAccessTokenHash { get; set; }
    public string? ProtectedPublicAccessToken { get; set; }
    public DigitalCheckoutStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ReservationExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(30);
    public DateTimeOffset? ProcessingStartedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public List<string> DigitalOrderIds { get; set; } = [];
    public List<string> LicenseOrderIds { get; set; } = [];
    public List<string> LicenseIds { get; set; } = [];
    public bool DeliveryEmailQueued { get; set; }
}

public sealed class DigitalCheckoutCollection
{
    public Dictionary<string, DigitalCheckout> Checkouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CustomerLoginChallenge
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string NormalizedEmail { get; set; } = "";
    public string ProtectedCode { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class CustomerLoginChallengeCollection
{
    public Dictionary<string, CustomerLoginChallenge> Challenges { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
    public required IReadOnlyList<StoreProductViewModel> Catalog { get; init; }
    public int CartCount { get; init; }
    public string? CustomerEmail { get; init; }
}

public sealed class StoreProductViewModel
{
    public required DigitalProductKind Kind { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required decimal Price { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public string? Badge { get; init; }
    public string? ImageUrl { get; init; }
    public string Meta { get; init; } = "";
}

public sealed class CartLineViewModel
{
    public required StoreProductViewModel Product { get; init; }
    public int Quantity { get; init; }
    public decimal Total => Product.Price * Quantity;
}

public sealed class DigitalCartViewModel
{
    public required string StoreId { get; init; }
    public required DigitalDownloadsSettings Settings { get; init; }
    public required IReadOnlyList<CartLineViewModel> Lines { get; init; }
    public string? CustomerEmail { get; init; }
    public decimal Total => Lines.Sum(line => line.Total);
}

public sealed class CustomerLoginViewModel
{
    public required string StoreId { get; init; }
    public required DigitalDownloadsSettings Settings { get; init; }
    public string ReturnUrl { get; init; } = "";
    public string Email { get; init; } = "";
    public bool CodeSent { get; init; }
    public string? Error { get; init; }
}

public sealed class DigitalPaymentViewModel
{
    public required string StoreId { get; init; }
    public required DigitalDownloadsSettings Settings { get; init; }
    public required DigitalCheckout Checkout { get; init; }
    public required string AccessToken { get; init; }
}

public sealed class CustomerDownloadViewModel
{
    public required DigitalOrder Order { get; init; }
    public required DigitalProduct Product { get; init; }
    public string? DownloadUrl { get; init; }
}

public sealed class CustomerLicenseViewModel
{
    public required ManagedLicense License { get; init; }
    public required LicenseProduct Product { get; init; }
    public string? LicenseKey { get; init; }
}

public sealed class CustomerLibraryViewModel
{
    public required string StoreId { get; init; }
    public required DigitalDownloadsSettings Settings { get; init; }
    public required string CustomerEmail { get; init; }
    public required IReadOnlyList<DigitalCheckout> Purchases { get; init; }
    public required IReadOnlyList<CustomerDownloadViewModel> Downloads { get; init; }
    public required IReadOnlyList<CustomerLicenseViewModel> Licenses { get; init; }
    public int CartCount { get; init; }
}

public sealed class DigitalPurchaseViewModel
{
    public required string StoreId { get; init; }
    public required DigitalDownloadsSettings Settings { get; init; }
    public required DigitalCheckout Checkout { get; init; }
    public required IReadOnlyList<CustomerDownloadViewModel> Downloads { get; init; }
    public required IReadOnlyList<CustomerLicenseViewModel> Licenses { get; init; }
    public required string AccessToken { get; init; }
    public bool CustomerAuthenticated { get; init; }
}

public sealed record DigitalPaymentStatus(string Status, string? RedirectUrl, string Message);

public sealed class OrderViewModel
{
    public required DigitalDownloadsSettings Settings { get; init; }
    public required DigitalProduct Product { get; init; }
    public required DigitalOrder Order { get; init; }
    public string? DownloadUrl { get; init; }
}
