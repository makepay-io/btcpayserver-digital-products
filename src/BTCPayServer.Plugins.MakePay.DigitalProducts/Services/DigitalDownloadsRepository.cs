#nullable enable
using System.Collections.Concurrent;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

public sealed class DigitalDownloadsRepository(StoreRepository stores)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<DigitalDownloadsSettings> GetSettings(string storeId) =>
        await stores.GetSettingAsync<DigitalDownloadsSettings>(storeId, DigitalProductsPlugin.SettingsKey) ?? new();

    public Task SaveSettings(string storeId, DigitalDownloadsSettings settings) =>
        stores.UpdateSetting(storeId, DigitalProductsPlugin.SettingsKey, settings);

    public async Task<IReadOnlyList<DigitalProduct>> GetProducts(string storeId) =>
        (await stores.GetSettingAsync<DigitalCatalog>(storeId, DigitalProductsPlugin.CatalogKey) ?? new()).Products
        .OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<DigitalProduct?> GetProduct(string storeId, string idOrSlug) =>
        (await GetProducts(storeId)).FirstOrDefault(product =>
            product.Id.Equals(idOrSlug, StringComparison.OrdinalIgnoreCase) ||
            product.Slug.Equals(idOrSlug, StringComparison.OrdinalIgnoreCase));

    public async Task SaveProduct(string storeId, DigitalProduct product)
    {
        await MutateCatalog(storeId, catalog =>
        {
            if (catalog.Products.Any(existing => existing.Id != product.Id && existing.Slug.Equals(product.Slug, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Product slug is already in use.");
            var index = catalog.Products.FindIndex(existing => existing.Id == product.Id);
            product.UpdatedAt = DateTimeOffset.UtcNow;
            if (index < 0) catalog.Products.Add(product); else catalog.Products[index] = product;
        });
    }

    public async Task DeleteProduct(string storeId, string productId) =>
        await MutateCatalog(storeId, catalog => catalog.Products.RemoveAll(product => product.Id == productId));

    public async Task<IReadOnlyList<DigitalOrder>> GetOrders(string storeId) =>
        (await stores.GetSettingAsync<DigitalOrderCollection>(storeId, DigitalProductsPlugin.OrdersKey) ?? new()).Orders.Values
        .OrderByDescending(order => order.CreatedAt).ToList();

    public async Task<DigitalOrder?> GetOrder(string storeId, string orderId) =>
        (await stores.GetSettingAsync<DigitalOrderCollection>(storeId, DigitalProductsPlugin.OrdersKey) ?? new()).Orders.GetValueOrDefault(orderId);

    public async Task<DigitalOrder?> FindOrderByInvoice(string storeId, string invoiceId) =>
        (await GetOrders(storeId)).FirstOrDefault(order => order.InvoiceId == invoiceId);

    public async Task SaveOrder(string storeId, DigitalOrder order) =>
        await MutateOrders(storeId, orders => orders.Orders[order.Id] = order);

    public async Task DeleteOrder(string storeId, string orderId) =>
        await MutateOrders(storeId, orders => orders.Orders.Remove(orderId));

    public async Task<DigitalOrder?> UpdateOrder(string storeId, string orderId, Func<DigitalOrder, bool> mutation)
    {
        DigitalOrder? result = null;
        await MutateOrders(storeId, orders =>
        {
            if (!orders.Orders.TryGetValue(orderId, out var order) || !mutation(order)) return;
            result = order;
        });
        return result;
    }

    private async Task MutateCatalog(string storeId, Action<DigitalCatalog> mutation)
    {
        var gate = _locks.GetOrAdd(storeId + ":catalog", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var catalog = await stores.GetSettingAsync<DigitalCatalog>(storeId, DigitalProductsPlugin.CatalogKey) ?? new();
            mutation(catalog);
            await stores.UpdateSetting(storeId, DigitalProductsPlugin.CatalogKey, catalog);
        }
        finally { gate.Release(); }
    }

    private async Task MutateOrders(string storeId, Action<DigitalOrderCollection> mutation)
    {
        var gate = _locks.GetOrAdd(storeId + ":orders", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var orders = await stores.GetSettingAsync<DigitalOrderCollection>(storeId, DigitalProductsPlugin.OrdersKey) ?? new();
            mutation(orders);
            await stores.UpdateSetting(storeId, DigitalProductsPlugin.OrdersKey, orders);
        }
        finally { gate.Release(); }
    }
}
