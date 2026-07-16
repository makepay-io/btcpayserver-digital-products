using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using BTCPayServer.Plugins.MakePay.LicenseManager;
using System.Text.Json;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class CombinedPluginTests
{
    [Fact]
    public void Release_version_metadata_is_synchronized()
    {
        const string expected = "1.6.3";
        var project = File.ReadAllText(CustomDomainGuideTests.RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.DigitalProducts",
            "BTCPayServer.Plugins.MakePay.DigitalProducts.csproj"));
        var plugin = File.ReadAllText(CustomDomainGuideTests.RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.DigitalProducts", "DigitalProductsPlugin.cs"));
        using var package = JsonDocument.Parse(File.ReadAllText(CustomDomainGuideTests.RepositoryFile(
            "packaging", "BTCPayServer.Plugins.MakePay.DigitalProducts.json")));

        Assert.Contains($"<Version>{expected}</Version>", project, StringComparison.Ordinal);
        Assert.Contains($"PluginVersion = \"{expected}\"", plugin, StringComparison.Ordinal);
        Assert.Equal(expected + ".0", package.RootElement.GetProperty("Version").GetString());
    }

    [Fact]
    public void Standalone_license_plugin_entry_point_is_not_present()
    {
        var assembly = typeof(DownloadTokenService).Assembly;
        Assert.Equal("BTCPayServer.Plugins.MakePay.DigitalProducts", assembly.GetName().Name);
        Assert.Null(assembly.GetType(
            "BTCPayServer.Plugins.MakePay.DigitalProducts.DigitalDownloadsPlugin",
            throwOnError: false));
        Assert.Null(assembly.GetType(
            "BTCPayServer.Plugins.MakePay.LicenseManager.LicenseManagerPlugin",
            throwOnError: false));
    }

    [Fact]
    public void Combined_plugin_retains_standalone_license_storage_keys()
    {
        Assert.Equal("MakePay.LicenseManager.Settings", LicenseManagerModule.SettingsKey);
        Assert.Equal("MakePay.LicenseManager.Products", LicenseManagerModule.ProductsKey);
        Assert.Equal("MakePay.LicenseManager.Licenses", LicenseManagerModule.LicensesKey);
        Assert.Equal("MakePay.LicenseManager.Orders", LicenseManagerModule.OrdersKey);
    }
}
