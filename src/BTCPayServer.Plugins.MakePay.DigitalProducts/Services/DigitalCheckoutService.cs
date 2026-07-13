#nullable enable
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

public sealed class DigitalCheckoutService
{
    public IReadOnlyList<StoreProductViewModel> BuildCatalog(
        IReadOnlyList<DigitalProduct> downloads,
        IReadOnlyList<LicenseProduct> licenses)
    {
        var result = downloads.Where(product => product.Active).Select(product => new StoreProductViewModel
        {
            Kind = DigitalProductKind.Download,
            Id = product.Id,
            Slug = product.Slug,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            CompareAtPrice = product.CompareAtPrice,
            Badge = product.Badge,
            ImageUrl = product.ImageUrl,
            ProductType = product.ProductType,
            DeliveryMode = product.DeliveryMode,
            HasPreview = product.PreviewEnabled && product.PreviewAssets.Count > 0,
            PreviewCount = product.PreviewEnabled ? product.PreviewAssets.Count : 0,
            DigitalProductSnapshot = DigitalProductSnapshot.From(product),
            Meta = DigitalStorefrontBuilder.ProductMeta(product)
        }).Concat(licenses.Where(product => product.Active).Select(product => new StoreProductViewModel
        {
            Kind = DigitalProductKind.License,
            Id = product.Id,
            Slug = product.Slug,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            CompareAtPrice = product.CompareAtPrice,
            Badge = product.Badge,
            ImageUrl = product.ImageUrl,
            Meta = $"{product.MaxActivations} activation{(product.MaxActivations == 1 ? "" : "s")} · {(product.DurationDays is null ? "Lifetime" : product.DurationDays + " days")}" 
        })).OrderBy(product => product.Kind).ThenBy(product => product.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return result;
    }

    public IReadOnlyList<CartLineViewModel> ResolveCart(DigitalCartState cart, IReadOnlyList<StoreProductViewModel> catalog) =>
        cart.Lines.Select(line =>
        {
            var product = catalog.FirstOrDefault(item => item.Kind == line.Kind && item.Id.Equals(line.ProductId, StringComparison.OrdinalIgnoreCase));
            return product is null ? null : new CartLineViewModel { Product = product, Quantity = line.Kind == DigitalProductKind.Download ? 1 : Math.Clamp(line.Quantity, 1, 10) };
        }).Where(line => line is not null).Cast<CartLineViewModel>().ToList();

    public DigitalCheckout Create(string storeId, string email, string currency, IReadOnlyList<CartLineViewModel> lines, string publicBaseUrl) => new()
    {
        StoreId = storeId,
        BuyerEmail = CustomerAccessService.NormalizeEmail(email),
        Currency = currency.ToUpperInvariant(),
        PublicBaseUrl = publicBaseUrl,
        Lines = lines.Select(line => new DigitalCheckoutLine
        {
            Kind = line.Product.Kind,
            ProductId = line.Product.Id,
            Name = line.Product.Name,
            Description = line.Product.Description,
            ImageUrl = line.Product.ImageUrl,
            Quantity = line.Quantity,
            UnitPrice = line.Product.Price,
            DigitalProductSnapshot = line.Product.Kind == DigitalProductKind.Download ? line.Product.DigitalProductSnapshot : null
        }).ToList(),
        Total = lines.Sum(line => line.Total),
        ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
    };
}
