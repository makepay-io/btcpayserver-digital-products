#nullable enable
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Encodings.Web;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;
using BTCPayServer.Plugins.MakePay.LicenseManager.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
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
    IRateLimitService rateLimits,
    DigitalCartService carts,
    DigitalCheckoutService checkoutService,
    CustomerAccessService access,
    LicenseSecurityService licenseSecurity,
    EmailSenderFactory emailFactory) : Controller
{
    [HttpGet("assets/hero")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult HeroAsset()
    {
        var stream = typeof(DigitalProductsPlugin).Assembly.GetManifestResourceStream(
            "BTCPayServer.Plugins.MakePay.DigitalProducts.Assets.makepay-digital-hero.png");
        return stream is null ? NotFound() : File(stream, "image/png");
    }

    [HttpGet("assets/product/{kind}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult ProductAsset(string kind)
    {
        var fileName = kind.Equals("license", StringComparison.OrdinalIgnoreCase)
            ? "makepay-license-cover.png"
            : "makepay-download-cover.png";
        var stream = typeof(DigitalProductsPlugin).Assembly.GetManifestResourceStream(
            "BTCPayServer.Plugins.MakePay.DigitalProducts.Assets." + fileName);
        return stream is null ? NotFound() : File(stream, "image/png");
    }

    [HttpGet("assets/storefront/{assetId}/{fileName}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult StorefrontAsset(string storeId, string assetId, string fileName)
    {
        var asset = files.OpenStorefrontAsset(storeId, assetId, fileName);
        if (asset is null) return NotFound();
        Response.RegisterForDisposeAsync(asset);
        Response.ContentLength = asset.Length;
        return File(asset.Stream, asset.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("")]
    public async Task<IActionResult> Storefront(string storeId, string? category, string? type)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var settings = await repository.GetSettings(storeId);
        var catalog = await Catalog(storeId);
        var categories = DigitalStorefrontBuilder.BuildCategories(settings, catalog);
        category ??= type?.Equals("downloads", StringComparison.OrdinalIgnoreCase) == true ? "downloads" :
            type?.Equals("licenses", StringComparison.OrdinalIgnoreCase) == true ? "licenses" : null;
        var selectedCategory = categories.FirstOrDefault(item => item.Slug.Equals(category, StringComparison.OrdinalIgnoreCase));
        var fallbackHeroUrl = Url.Action(nameof(HeroAsset), new { storeId })!;
        return View("~/Views/DigitalDownloads/Public/Storefront.cshtml", new StorefrontViewModel
        {
            StoreId = storeId,
            Settings = settings,
            Products = await repository.GetProducts(storeId),
            LicenseSettings = await licenses.GetSettings(storeId),
            LicenseProducts = await licenses.GetProducts(storeId),
            Catalog = DigitalStorefrontBuilder.FilterCatalog(catalog, selectedCategory),
            HeroSlides = DigitalStorefrontBuilder.BuildHeroSlides(settings, catalog, fallbackHeroUrl,
                product => Url.Action(nameof(Product), new
                {
                    storeId,
                    kind = DigitalStorefrontBuilder.ProductKindSegment(product),
                    productId = product.Slug
                })!),
            Categories = categories,
            ActiveCategorySlug = selectedCategory?.Slug,
            CartCount = Cart(storeId).Lines.Sum(line => line.Quantity),
            CustomerEmail = CustomerEmail(storeId)
        });
    }

    [HttpGet("products/{kind}/{productId}")]
    public async Task<IActionResult> Product(string storeId, string kind, string productId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var productKind = kind.Equals("download", StringComparison.OrdinalIgnoreCase)
            ? DigitalProductKind.Download
            : kind.Equals("license", StringComparison.OrdinalIgnoreCase)
                ? DigitalProductKind.License
                : (DigitalProductKind?)null;
        if (productKind is null) return NotFound();

        var settings = await repository.GetSettings(storeId);
        var catalog = await Catalog(storeId);
        var product = catalog.FirstOrDefault(item =>
            item.Kind == productKind &&
            (item.Id.Equals(productId, StringComparison.OrdinalIgnoreCase) ||
             item.Slug.Equals(productId, StringComparison.OrdinalIgnoreCase)));
        if (product is null) return NotFound();

        var categories = DigitalStorefrontBuilder.BuildCategories(settings, catalog);
        var productReference = DigitalStorefrontBuilder.ProductReference(product);
        var relatedReferences = categories
            .Where(category => category.ProductReferences.Contains(productReference))
            .SelectMany(category => category.ProductReferences)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var related = catalog
            .Where(item => !item.Id.Equals(product.Id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => relatedReferences.Contains(DigitalStorefrontBuilder.ProductReference(item)))
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return View("~/Views/DigitalDownloads/Public/Product.cshtml", new DigitalProductDetailViewModel
        {
            StoreId = storeId,
            Settings = settings,
            Product = product,
            Categories = categories,
            RelatedProducts = related,
            CartCount = Cart(storeId).Lines.Sum(line => line.Quantity),
            CustomerEmail = CustomerEmail(storeId)
        });
    }

    [HttpPost("cart/items")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToCart(string storeId, DigitalProductKind kind, string productId, int quantity = 1, string? returnUrl = null)
    {
        var catalog = await Catalog(storeId);
        if (catalog.All(product => product.Kind != kind || !product.Id.Equals(productId, StringComparison.OrdinalIgnoreCase))) return NotFound();
        var cart = Cart(storeId);
        carts.Add(cart, kind, productId, quantity);
        SaveCart(storeId, cart);
        return Redirect(LocalReturn(returnUrl, Url.Action(nameof(CartPage), new { storeId })!));
    }

    [HttpGet("cart")]
    public async Task<IActionResult> CartPage(string storeId)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        return View("~/Views/DigitalDownloads/Public/Cart.cshtml", new DigitalCartViewModel
        {
            StoreId = storeId,
            Settings = await repository.GetSettings(storeId),
            Lines = checkoutService.ResolveCart(Cart(storeId), await Catalog(storeId)),
            CustomerEmail = CustomerEmail(storeId)
        });
    }

    [HttpPost("cart/update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCart(string storeId, DigitalProductKind kind, string productId, int quantity)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var cart = Cart(storeId);
        carts.Update(cart, kind, productId, quantity);
        SaveCart(storeId, cart);
        return RedirectToAction(nameof(CartPage), new { storeId });
    }

    [HttpGet("login")]
    public async Task<IActionResult> Login(string storeId, string? returnUrl, string? email, bool codeSent = false)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var target = LocalReturn(returnUrl, Url.Action(nameof(Account), new { storeId })!);
        if (CustomerEmail(storeId) is not null) return Redirect(target);
        return View("~/Views/DigitalDownloads/Public/Login.cshtml", new CustomerLoginViewModel
        {
            StoreId = storeId,
            Settings = await repository.GetSettings(storeId),
            ReturnUrl = target,
            Email = email?.Trim() ?? "",
            CodeSent = codeSent
        });
    }

    [HttpPost("login/request")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestLoginCode(string storeId, string email, string? returnUrl, CancellationToken cancellationToken)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var settings = await repository.GetSettings(storeId);
        var target = LocalReturn(returnUrl, Url.Action(nameof(Account), new { storeId })!);
        if (!settings.CustomerAccountsEnabled || !new EmailAddressAttribute().IsValid(email))
            return LoginError(storeId, settings, target, email, "Enter a valid email address.");
        var zone = $"makepay-dp-login-{storeId}-{CustomerAccessService.NormalizeEmail(email)}-{HttpContext.Connection.RemoteIpAddress}";
        if (!await rateLimits.Throttle(ZoneLimits.PublicInvoices, zone, cancellationToken)) return StatusCode(StatusCodes.Status429TooManyRequests);
        var created = access.CreateChallenge(email, settings.LoginCodeMinutes);
        await repository.SaveLoginChallenge(storeId, created.Challenge);
        try
        {
            string E(string value) => HtmlEncoder.Default.Encode(value);
            var body = settings.LoginEmailHtml
                .Replace("{StoreName}", E(settings.StorefrontTitle), StringComparison.Ordinal)
                .Replace("{Code}", E(created.Code), StringComparison.Ordinal)
                .Replace("{Minutes}", settings.LoginCodeMinutes.ToString(), StringComparison.Ordinal);
            var subject = settings.LoginEmailSubject.Replace("{StoreName}", settings.StorefrontTitle, StringComparison.Ordinal);
            (await emailFactory.GetEmailSender(storeId)).SendEmail(MailboxAddress.Parse(email.Trim()), subject, body);
        }
        catch
        {
            return LoginError(storeId, settings, target, email, "The sign-in email could not be sent. Ask the merchant to check the BTCPay store email settings.");
        }
        return RedirectToAction(nameof(Login), new { storeId, returnUrl = target, email = email.Trim(), codeSent = true });
    }

    [HttpPost("login/verify")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyLoginCode(string storeId, string email, string code, string? returnUrl)
    {
        if (await stores.FindStore(storeId) is null) return NotFound();
        var settings = await repository.GetSettings(storeId);
        var target = LocalReturn(returnUrl, Url.Action(nameof(Account), new { storeId })!);
        if (!new EmailAddressAttribute().IsValid(email)) return LoginError(storeId, settings, target, email, "Enter a valid email address.");
        var normalized = CustomerAccessService.NormalizeEmail(email);
        var challenge = await repository.GetLatestLoginChallenge(storeId, normalized);
        var verified = false;
        if (challenge is not null)
        {
            await repository.UpdateLoginChallenge(storeId, challenge.Id, current =>
            {
                if (access.Verify(current, code)) { current.ConsumedAt = DateTimeOffset.UtcNow; verified = true; }
                else current.Attempts++;
                return true;
            });
        }
        if (!verified) return LoginError(storeId, settings, target, email, "That code is invalid or expired. Request a new code and try again.", true);
        Response.Cookies.Append(SessionCookie(storeId), access.CreateSession(storeId, normalized, settings.CustomerSessionHours), CookieOptions(settings.CustomerSessionHours));
        return Redirect(target);
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public IActionResult Logout(string storeId)
    {
        Response.Cookies.Delete(SessionCookie(storeId), new CookieOptions { Path = "/", Secure = Request.IsHttps, SameSite = SameSiteMode.Lax });
        return RedirectToAction(nameof(Storefront), new { storeId });
    }

    [HttpPost("checkout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(string storeId, CancellationToken cancellationToken)
    {
        var email = CustomerEmail(storeId);
        if (email is null) return RedirectToAction(nameof(Login), new { storeId, returnUrl = Url.Action(nameof(CartPage), new { storeId }) });
        var zone = $"makepay-dp-checkout-{storeId}-{HttpContext.Connection.RemoteIpAddress}";
        if (!await rateLimits.Throttle(ZoneLimits.PublicInvoices, zone, cancellationToken)) return StatusCode(StatusCodes.Status429TooManyRequests);
        var store = await stores.FindStore(storeId);
        if (store is null) return NotFound();
        var settings = await repository.GetSettings(storeId);
        var lines = checkoutService.ResolveCart(Cart(storeId), await Catalog(storeId));
        if (lines.Count == 0) return RedirectToAction(nameof(CartPage), new { storeId });
        var checkout = checkoutService.Create(storeId, email, settings.Currency, lines, Request.GetAbsoluteRoot());
        var accessToken = access.CreateCheckoutAccess(checkout);
        await repository.SaveCheckout(storeId, checkout);
        try
        {
            var successUrl = Url.ActionLink(nameof(Purchase), values: new { storeId, checkoutId = checkout.Id, accessToken })!;
            var invoice = await invoices.CreateInvoiceCoreRaw(new CreateInvoiceRequest
            {
                Amount = checkout.Total,
                Currency = checkout.Currency,
                Metadata = new InvoiceMetadata
                {
                    BuyerEmail = checkout.BuyerEmail,
                    ItemCode = checkout.Id,
                    ItemDesc = checkout.Lines.Count == 1 ? checkout.Lines[0].Name : $"{checkout.Lines.Sum(line => line.Quantity)} digital products",
                    OrderId = checkout.Id,
                    OrderUrl = successUrl
                }.ToJObject(),
                Checkout = new InvoiceDataBase.CheckoutOptions { RedirectAutomatically = true, RedirectURL = successUrl }
            }, store, Request.GetAbsoluteRoot(), [DigitalCheckoutFulfillmentService.Tag(checkout.Id)], cancellationToken);
            checkout.InvoiceId = invoice.Id;
            await repository.SaveCheckout(storeId, checkout);
            SaveCart(storeId, new());
            return RedirectToAction(nameof(Payment), new { storeId, checkoutId = checkout.Id, accessToken });
        }
        catch
        {
            await repository.UpdateCheckout(storeId, checkout.Id, current => { current.Status = DigitalCheckoutStatus.Cancelled; return true; });
            throw;
        }
    }

    [HttpGet("checkout/{checkoutId}/payment")]
    public async Task<IActionResult> Payment(string storeId, string checkoutId, string? accessToken)
    {
        var checkout = await repository.GetCheckout(storeId, checkoutId);
        if (checkout is null || !access.CanAccess(checkout, accessToken, CustomerEmail(storeId))) return NotFound();
        if (checkout.Status == DigitalCheckoutStatus.Paid) return RedirectToAction(nameof(Purchase), new { storeId, checkoutId, accessToken });
        return View("~/Views/DigitalDownloads/Public/Payment.cshtml", new DigitalPaymentViewModel { StoreId = storeId, Settings = await repository.GetSettings(storeId), Checkout = checkout, AccessToken = accessToken ?? access.RecoverCheckoutAccess(checkout) ?? "" });
    }

    [HttpGet("checkout/{checkoutId}/status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CheckoutStatus(string storeId, string checkoutId, string? accessToken)
    {
        var checkout = await repository.GetCheckout(storeId, checkoutId);
        if (checkout is null || !access.CanAccess(checkout, accessToken, CustomerEmail(storeId))) return NotFound();
        return checkout.Status switch
        {
            DigitalCheckoutStatus.Paid => Json(new DigitalPaymentStatus("paid", Url.Action(nameof(Purchase), new { storeId, checkoutId, accessToken }), "Your products are ready.")),
            DigitalCheckoutStatus.Cancelled => Json(new DigitalPaymentStatus("cancelled", null, "This invoice is no longer payable.")),
            _ => Json(new DigitalPaymentStatus("pending", null, "Waiting for BTCPay confirmation."))
        };
    }

    [HttpGet("purchase/{checkoutId}")]
    public async Task<IActionResult> Purchase(string storeId, string checkoutId, string? accessToken)
    {
        var checkout = await repository.GetCheckout(storeId, checkoutId);
        var sessionEmail = CustomerEmail(storeId);
        if (checkout is null || !access.CanAccess(checkout, accessToken, sessionEmail)) return NotFound();
        return View("~/Views/DigitalDownloads/Public/Purchase.cshtml", new DigitalPurchaseViewModel
        {
            StoreId = storeId,
            Settings = await repository.GetSettings(storeId),
            Checkout = checkout,
            Downloads = await DownloadItems(storeId, checkout.DigitalOrderIds),
            Licenses = await LicenseItems(storeId, checkout.LicenseIds),
            AccessToken = accessToken ?? access.RecoverCheckoutAccess(checkout) ?? "",
            CustomerAuthenticated = sessionEmail is not null
        });
    }

    [HttpGet("account")]
    public async Task<IActionResult> Account(string storeId)
    {
        var email = CustomerEmail(storeId);
        if (email is null) return RedirectToAction(nameof(Login), new { storeId, returnUrl = Url.Action(nameof(Account), new { storeId }) });
        var checkouts = (await repository.GetCheckouts(storeId)).Where(checkout => CustomerAccessService.NormalizeEmail(checkout.BuyerEmail) == email).ToList();
        var downloadOrders = (await repository.GetOrders(storeId)).Where(order => CustomerAccessService.NormalizeEmail(order.BuyerEmail) == email && order.Status == DigitalOrderStatus.Paid).ToList();
        var managedLicenses = (await licenses.GetLicenses(storeId)).Where(license => CustomerAccessService.NormalizeEmail(license.CustomerEmail) == email).ToList();
        return View("~/Views/DigitalDownloads/Public/Account.cshtml", new CustomerLibraryViewModel
        {
            StoreId = storeId,
            Settings = await repository.GetSettings(storeId),
            CustomerEmail = email,
            Purchases = checkouts,
            Downloads = await DownloadItems(storeId, downloadOrders.Select(order => order.Id)),
            Licenses = await LicenseItems(storeId, managedLicenses.Select(license => license.Id)),
            CartCount = Cart(storeId).Lines.Sum(line => line.Quantity)
        });
    }

    // Legacy single-product route retained for existing integrations.
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
        catch { await repository.DeleteOrder(storeId, order.Id); throw; }
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

    private async Task<IReadOnlyList<StoreProductViewModel>> Catalog(string storeId) =>
        checkoutService.BuildCatalog(await repository.GetProducts(storeId), await licenses.GetProducts(storeId));

    private DigitalCartState Cart(string storeId) => carts.Read(storeId, Request.Cookies[CartCookie(storeId)]);
    private string? CustomerEmail(string storeId) => access.ReadSession(Request.Cookies[SessionCookie(storeId)], storeId);

    private void SaveCart(string storeId, DigitalCartState cart) =>
        Response.Cookies.Append(CartCookie(storeId), carts.Protect(storeId, cart), CookieOptions(720));

    private static string CartCookie(string storeId) => $"mpdp-cart-{storeId[..Math.Min(16, storeId.Length)]}";
    private static string SessionCookie(string storeId) => $"mpdp-customer-{storeId[..Math.Min(16, storeId.Length)]}";
    private CookieOptions CookieOptions(int hours) => new() { HttpOnly = true, IsEssential = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromHours(hours), Path = "/" };
    private string LocalReturn(string? value, string fallback) => !string.IsNullOrWhiteSpace(value) && Url.IsLocalUrl(value) ? value : fallback;

    private IActionResult LoginError(string storeId, DigitalDownloadsSettings settings, string returnUrl, string email, string error, bool codeSent = false) =>
        View("~/Views/DigitalDownloads/Public/Login.cshtml", new CustomerLoginViewModel { StoreId = storeId, Settings = settings, ReturnUrl = returnUrl, Email = email.Trim(), CodeSent = codeSent, Error = error });

    private async Task<IReadOnlyList<CustomerDownloadViewModel>> DownloadItems(string storeId, IEnumerable<string> orderIds)
    {
        var result = new List<CustomerDownloadViewModel>();
        foreach (var orderId in orderIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var order = await repository.GetOrder(storeId, orderId);
            if (order is null) continue;
            var product = await repository.GetProduct(storeId, order.ProductId);
            if (product is null) continue;
            string? url = null;
            if (order.Status == DigitalOrderStatus.Paid && tokens.Unprotect(order.ProtectedToken) is { } token)
                url = Url.Action(nameof(Download), new { storeId, orderId = order.Id, token });
            result.Add(new CustomerDownloadViewModel { Order = order, Product = product, DownloadUrl = url });
        }
        return result;
    }

    private async Task<IReadOnlyList<CustomerLicenseViewModel>> LicenseItems(string storeId, IEnumerable<string> licenseIds)
    {
        var result = new List<CustomerLicenseViewModel>();
        foreach (var licenseId in licenseIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var license = await licenses.GetLicense(storeId, licenseId);
            if (license is null) continue;
            var product = await licenses.GetProduct(storeId, license.ProductId);
            if (product is null) continue;
            result.Add(new CustomerLicenseViewModel { License = license, Product = product, LicenseKey = licenseSecurity.Unprotect(license.ProtectedKey) });
        }
        return result;
    }
}
