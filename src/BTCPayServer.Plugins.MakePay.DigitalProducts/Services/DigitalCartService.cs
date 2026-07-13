#nullable enable
using System.Text.Json;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using Microsoft.AspNetCore.DataProtection;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

public sealed class DigitalCartService(IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("MakePay.DigitalProducts.Cart.v1");

    public DigitalCartState Read(string storeId, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new();
        try
        {
            var envelope = JsonSerializer.Deserialize<CartEnvelope>(_protector.Unprotect(value));
            if (envelope is null || envelope.StoreId != storeId || envelope.Cart.UpdatedAt < DateTimeOffset.UtcNow.AddDays(-30)) return new();
            envelope.Cart.Lines = envelope.Cart.Lines
                .Where(line => !string.IsNullOrWhiteSpace(line.ProductId) && line.Quantity is >= 1 and <= 10)
                .GroupBy(line => (line.Kind, line.ProductId), new CartLineKeyComparer())
                .Select(group => new DigitalCartLine { Kind = group.Key.Kind, ProductId = group.Key.ProductId, Quantity = Math.Min(10, group.Sum(line => line.Quantity)) })
                .Take(30).ToList();
            return envelope.Cart;
        }
        catch { return new(); }
    }

    public string Protect(string storeId, DigitalCartState cart)
    {
        cart.UpdatedAt = DateTimeOffset.UtcNow;
        return _protector.Protect(JsonSerializer.Serialize(new CartEnvelope(storeId, cart)));
    }

    public void Add(DigitalCartState cart, DigitalProductKind kind, string productId, int quantity)
    {
        var existing = cart.Lines.FirstOrDefault(line => line.Kind == kind && line.ProductId.Equals(productId, StringComparison.OrdinalIgnoreCase));
        if (existing is null) cart.Lines.Add(new DigitalCartLine { Kind = kind, ProductId = productId, Quantity = kind == DigitalProductKind.Download ? 1 : Math.Clamp(quantity, 1, 10) });
        else existing.Quantity = kind == DigitalProductKind.Download ? 1 : Math.Clamp(existing.Quantity + quantity, 1, 10);
    }

    public void Update(DigitalCartState cart, DigitalProductKind kind, string productId, int quantity)
    {
        var existing = cart.Lines.FirstOrDefault(line => line.Kind == kind && line.ProductId.Equals(productId, StringComparison.OrdinalIgnoreCase));
        if (existing is null) return;
        if (quantity <= 0) cart.Lines.Remove(existing);
        else existing.Quantity = kind == DigitalProductKind.Download ? 1 : Math.Clamp(quantity, 1, 10);
    }

    private sealed record CartEnvelope(string StoreId, DigitalCartState Cart);

    private sealed class CartLineKeyComparer : IEqualityComparer<(DigitalProductKind Kind, string ProductId)>
    {
        public bool Equals((DigitalProductKind Kind, string ProductId) x, (DigitalProductKind Kind, string ProductId) y) =>
            x.Kind == y.Kind && x.ProductId.Equals(y.ProductId, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((DigitalProductKind Kind, string ProductId) obj) => HashCode.Combine(obj.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ProductId));
    }
}
