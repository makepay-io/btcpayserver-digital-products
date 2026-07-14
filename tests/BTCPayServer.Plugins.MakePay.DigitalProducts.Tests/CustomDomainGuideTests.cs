#nullable enable
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class CustomDomainGuideTests
{
    [Fact]
    public void Settings_explain_native_app_mapping_and_unavoidable_server_setup()
    {
        var source = File.ReadAllText(RepositoryFile(
            "src",
            "BTCPayServer.Plugins.MakePay.DigitalProducts",
            "Views",
            "DigitalDownloads",
            "Settings.cshtml"));

        Assert.Contains("Current canonical URL", source, StringComparison.Ordinal);
        Assert.Contains("/stores/@storeId/downloads", source, StringComparison.Ordinal);
        Assert.Contains("Create Digital Products app", source, StringComparison.Ordinal);
        Assert.Contains("Server settings → Policies → Domain mapping", source, StringComparison.Ordinal);
        Assert.Contains("native domain mapping resolves the request to AppData", source, StringComparison.Ordinal);
        Assert.Contains("Query and form values cannot select another store", source, StringComparison.Ordinal);
        Assert.Contains("safe GET/HEAD visits are canonicalized", source, StringComparison.Ordinal);
        Assert.Contains("exact normalized ASCII hostname", source, StringComparison.Ordinal);
        Assert.Contains("BTCPay does not reject them", source, StringComparison.Ordinal);
        Assert.Contains("always selects the first matching row", source, StringComparison.Ordinal);
        Assert.Contains(".onion", source, StringComparison.Ordinal);
        Assert.Contains("BTCPAY_ADDITIONAL_HOSTS", source, StringComparison.Ordinal);
        Assert.Contains("complete comma-separated list", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CNAME alone is insufficient", source, StringComparison.Ordinal);
        Assert.Contains("can prevent Let's Encrypt renewal for every hostname", source, StringComparison.Ordinal);
        Assert.Contains("aliases the whole BTCPay Server, not only this store", source, StringComparison.Ordinal);
        Assert.Contains("does not claim hostnames or change server TLS settings", source, StringComparison.Ordinal);
        Assert.Contains("https://docs.btcpayserver.org/Docker/#environment-variables", source, StringComparison.Ordinal);
        Assert.Contains("https://docs.btcpayserver.org/FAQ/Apps/#how-to-map-a-domain-name-to-an-app", source, StringComparison.Ordinal);
        Assert.Contains("https://docs.btcpayserver.org/FAQ/Deployment/#can-i-use-an-existing-nginx-server-as-a-reverse-proxy-with-ssl-termination", source, StringComparison.Ordinal);

        Assert.DoesNotContain("name=\"CleanDomain", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("name=\"CustomDomain", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_preserves_the_same_native_mapping_safety_contract()
    {
        var source = File.ReadAllText(RepositoryFile("README.md"));

        Assert.Contains("## Custom domains", source, StringComparison.Ordinal);
        Assert.Contains("https://shop.example.com/downloads", source, StringComparison.Ordinal);
        Assert.Contains("complete comma-separated list", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A CNAME alone is insufficient", source, StringComparison.Ordinal);
        Assert.Contains("aliases the entire BTCPay Server, not only this store", source, StringComparison.Ordinal);
        Assert.Contains("Server settings → Policies → Domain mapping", source, StringComparison.Ordinal);
        Assert.Contains("AppData.StoreDataId", source, StringComparison.Ordinal);
        Assert.Contains("safe GET/HEAD requests are canonicalized", source, StringComparison.Ordinal);
        Assert.Contains("configured BTCPay root path", source, StringComparison.Ordinal);
        Assert.Contains("normalized ASCII/punycode", source, StringComparison.Ordinal);
        Assert.Contains("first row in the global Policies list wins", source, StringComparison.Ordinal);
        Assert.Contains("current `.onion` origin", source, StringComparison.Ordinal);
        Assert.Contains("cannot be configured from inside the plugin", source, StringComparison.Ordinal);
        Assert.Contains("a dedicated `shop.` subdomain is recommended", source, StringComparison.OrdinalIgnoreCase);
    }

    internal static string RepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file {Path.Combine(segments)}.");
    }
}
