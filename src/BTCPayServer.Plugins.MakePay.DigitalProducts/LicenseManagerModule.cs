#nullable enable

namespace BTCPayServer.Plugins.MakePay.LicenseManager;

/// <summary>
/// Storage keys retained from the standalone License Manager plugin so existing
/// store products, licenses, API configuration, and orders migrate in place.
/// </summary>
public static class LicenseManagerModule
{
    public const string SettingsKey = "MakePay.LicenseManager.Settings";
    public const string ProductsKey = "MakePay.LicenseManager.Products";
    public const string LicensesKey = "MakePay.LicenseManager.Licenses";
    public const string OrdersKey = "MakePay.LicenseManager.Orders";
}
