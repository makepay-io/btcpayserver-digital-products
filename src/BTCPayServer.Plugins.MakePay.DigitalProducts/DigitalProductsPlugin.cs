#nullable enable
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using BTCPayServer.Plugins.MakePay.LicenseManager.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts;

public sealed class DigitalProductsPlugin : BaseBTCPayServerPlugin
{
    public const string PluginVersion = "1.4.2";
    public const string SettingsKey = "MakePay.DigitalDownloads.Settings";
    public const string CatalogKey = "MakePay.DigitalDownloads.Catalog";
    public const string OrdersKey = "MakePay.DigitalDownloads.Orders";
    public const string CheckoutsKey = "MakePay.DigitalProducts.Checkouts";
    public const string LoginChallengesKey = "MakePay.DigitalProducts.LoginChallenges";

    public override string Identifier => "BTCPayServer.Plugins.MakePay.DigitalProducts";
    public override string Name => "MakePay Digital Products";
    public override string Description => "Sell files, ebooks, audio, video, photos, art, and generated software licenses from one BTCPay plugin.";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.3.5" }
    ];

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<DigitalDownloadsRepository>();
        services.AddSingleton<DownloadTokenService>();
        services.AddSingleton<CustomerAccessService>();
        services.AddSingleton<DigitalCartService>();
        services.AddSingleton<DigitalCheckoutService>();
        services.AddHttpClient<ProductFileService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<DigitalDeliveryService>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<DigitalDeliveryService>());
        services.AddSingleton<LicenseRepository>();
        services.AddSingleton<LicenseKeyGenerator>();
        services.AddSingleton<LicenseSecurityService>();
        services.AddSingleton<LicenseFulfillmentService>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<LicenseFulfillmentService>());
        services.AddSingleton<DigitalCheckoutFulfillmentService>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<DigitalCheckoutFulfillmentService>());
        services.AddUIExtension("store-integrations-nav", "DigitalDownloads/StoreNavExtension");
        base.Execute(services);
    }
}
