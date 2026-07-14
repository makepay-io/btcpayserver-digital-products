#nullable enable
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class CustomDomainGuideTests
{
    [Fact]
    public void Settings_explain_the_supported_route_and_unavoidable_server_setup()
    {
        var source = File.ReadAllText(RepositoryFile(
            "src",
            "BTCPayServer.Plugins.MakePay.DigitalProducts",
            "Views",
            "DigitalDownloads",
            "Settings.cshtml"));

        Assert.Contains("Current canonical URL", source, StringComparison.Ordinal);
        Assert.Contains("/stores/&lt;storeId&gt;/downloads", source, StringComparison.Ordinal);
        Assert.Contains("BTCPAY_ADDITIONAL_HOSTS", source, StringComparison.Ordinal);
        Assert.Contains("complete comma-separated list", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CNAME alone is insufficient", source, StringComparison.Ordinal);
        Assert.Contains("can prevent Let's Encrypt renewal for every hostname", source, StringComparison.Ordinal);
        Assert.Contains("aliases the whole BTCPay Server, not only this store", source, StringComparison.Ordinal);
        Assert.Contains("do not remove the store-scoped route", source, StringComparison.Ordinal);
        Assert.Contains("not currently registered as a BTCPay App", source, StringComparison.Ordinal);
        Assert.Contains("cannot be configured from inside the BTCPay plugin", source, StringComparison.Ordinal);
        Assert.Contains("https://docs.btcpayserver.org/Docker/#environment-variables", source, StringComparison.Ordinal);
        Assert.Contains("https://docs.btcpayserver.org/FAQ/Apps/#how-to-map-a-domain-name-to-an-app", source, StringComparison.Ordinal);
        Assert.Contains("https://docs.btcpayserver.org/FAQ/Deployment/#can-i-use-an-existing-nginx-server-as-a-reverse-proxy-with-ssl-termination", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_preserves_the_same_custom_domain_safety_contract()
    {
        var source = File.ReadAllText(RepositoryFile("README.md"));

        Assert.Contains("## Custom domains", source, StringComparison.Ordinal);
        Assert.Contains("https://shop.example.com/stores/<storeId>/downloads", source, StringComparison.Ordinal);
        Assert.Contains("complete comma-separated list", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A CNAME alone is insufficient", source, StringComparison.Ordinal);
        Assert.Contains("aliases the entire BTCPay Server, not only this store", source, StringComparison.Ordinal);
        Assert.Contains("do not remove `/stores/<storeId>`", source, StringComparison.Ordinal);
        Assert.Contains("a dedicated subdomain is recommended", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file {Path.Combine(segments)}.");
    }
}
