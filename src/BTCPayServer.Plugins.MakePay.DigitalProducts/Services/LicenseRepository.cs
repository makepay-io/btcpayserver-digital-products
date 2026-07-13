#nullable enable
using System.Collections.Concurrent;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Plugins.MakePay.LicenseManager.Services;

public sealed class LicenseRepository(StoreRepository stores)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    public async Task<LicenseManagerSettings> GetSettings(string storeId) => await stores.GetSettingAsync<LicenseManagerSettings>(storeId, LicenseManagerModule.SettingsKey) ?? new();
    public Task SaveSettings(string storeId, LicenseManagerSettings value) => stores.UpdateSetting(storeId, LicenseManagerModule.SettingsKey, value);
    public async Task<IReadOnlyList<LicenseProduct>> GetProducts(string storeId) => (await stores.GetSettingAsync<LicenseProductCollection>(storeId, LicenseManagerModule.ProductsKey) ?? new()).Products.OrderBy(p => p.Name).ToList();
    public async Task<LicenseProduct?> GetProduct(string storeId, string idOrSlug) => (await GetProducts(storeId)).FirstOrDefault(p => p.Id.Equals(idOrSlug, StringComparison.OrdinalIgnoreCase) || p.Slug.Equals(idOrSlug, StringComparison.OrdinalIgnoreCase));
    public async Task SaveProduct(string storeId, LicenseProduct product) => await Mutate<LicenseProductCollection>(storeId, LicenseManagerModule.ProductsKey, value =>
    {
        if (value.Products.Any(p => p.Id != product.Id && p.Slug.Equals(product.Slug, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Product slug is already in use.");
        var index = value.Products.FindIndex(p => p.Id == product.Id); product.UpdatedAt = DateTimeOffset.UtcNow; if (index < 0) value.Products.Add(product); else value.Products[index] = product;
    });
    public async Task<IReadOnlyList<ManagedLicense>> GetLicenses(string storeId) => (await stores.GetSettingAsync<ManagedLicenseCollection>(storeId, LicenseManagerModule.LicensesKey) ?? new()).Licenses.Values.OrderByDescending(l => l.IssuedAt).ToList();
    public async Task<ManagedLicense?> GetLicense(string storeId, string id) => (await stores.GetSettingAsync<ManagedLicenseCollection>(storeId, LicenseManagerModule.LicensesKey) ?? new()).Licenses.GetValueOrDefault(id);
    public async Task<ManagedLicense?> FindByKeyHash(string storeId, string keyHash) => (await GetLicenses(storeId)).FirstOrDefault(l => l.KeyHash == keyHash);
    public async Task SaveLicense(string storeId, ManagedLicense license) => await Mutate<ManagedLicenseCollection>(storeId, LicenseManagerModule.LicensesKey, value => value.Licenses[license.Id] = license);
    public async Task<ManagedLicense?> UpdateLicense(string storeId, string id, Func<ManagedLicense, bool> update)
    {
        ManagedLicense? result = null;
        await Mutate<ManagedLicenseCollection>(storeId, LicenseManagerModule.LicensesKey, value => { if (value.Licenses.TryGetValue(id, out var license) && update(license)) result = license; });
        return result;
    }
    public async Task<IReadOnlyList<LicenseOrder>> GetOrders(string storeId) => (await stores.GetSettingAsync<LicenseOrderCollection>(storeId, LicenseManagerModule.OrdersKey) ?? new()).Orders.Values.OrderByDescending(o => o.CreatedAt).ToList();
    public async Task<LicenseOrder?> GetOrder(string storeId, string id) => (await stores.GetSettingAsync<LicenseOrderCollection>(storeId, LicenseManagerModule.OrdersKey) ?? new()).Orders.GetValueOrDefault(id);
    public async Task SaveOrder(string storeId, LicenseOrder order) => await Mutate<LicenseOrderCollection>(storeId, LicenseManagerModule.OrdersKey, value => value.Orders[order.Id] = order);
    public async Task DeleteOrder(string storeId, string id) => await Mutate<LicenseOrderCollection>(storeId, LicenseManagerModule.OrdersKey, value => value.Orders.Remove(id));

    private async Task Mutate<T>(string storeId, string key, Action<T> update) where T : class, new()
    {
        var gate = _locks.GetOrAdd(storeId + ":" + key, _ => new SemaphoreSlim(1, 1)); await gate.WaitAsync();
        try { var value = await stores.GetSettingAsync<T>(storeId, key) ?? new(); update(value); await stores.UpdateSetting(storeId, key, value); }
        finally { gate.Release(); }
    }
}
