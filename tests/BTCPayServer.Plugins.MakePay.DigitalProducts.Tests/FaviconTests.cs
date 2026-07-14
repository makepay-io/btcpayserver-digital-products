#nullable enable
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class FaviconTests
{
    [Theory]
    [InlineData(" https://cdn.example.com/brand/icon.png ", "https://cdn.example.com/brand/icon.png")]
    [InlineData("http://localhost/assets/favicon.ico", "http://localhost/assets/favicon.ico")]
    [InlineData("/stores/demo/downloads/assets/storefront/favicon/icon.ico", "/stores/demo/downloads/assets/storefront/favicon/icon.ico")]
    public void Safe_favicon_urls_are_trimmed_and_preserved(string value, string expected) =>
        Assert.Equal(expected, DigitalStorefrontBuilder.SafePublicResourceUrl(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AA==")]
    [InlineData("ftp://example.com/icon.png")]
    [InlineData("//evil.example/icon.png")]
    [InlineData("/\\evil.example/icon.png")]
    [InlineData("relative/icon.png")]
    public void Empty_or_unsafe_favicon_urls_do_not_render(string? value) =>
        Assert.Null(DigitalStorefrontBuilder.SafePublicResourceUrl(value));

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/x-icon")]
    [InlineData("image/vnd.microsoft.icon")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    [InlineData("image/gif")]
    public void Favicon_upload_validation_accepts_browser_icon_formats(string contentType) =>
        Assert.Null(ProductFileService.ValidateFaviconAsset(Upload([1], contentType)));

    [Fact]
    public void Favicon_upload_validation_rejects_active_empty_and_oversized_files()
    {
        Assert.Contains("not accepted", ProductFileService.ValidateFaviconAsset(Upload([1], "image/svg+xml")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("empty", ProductFileService.ValidateFaviconAsset(Upload([], "image/png")), StringComparison.OrdinalIgnoreCase);

        var oversized = new FormFile(new MemoryStream([1]), 0, 1024 * 1024 + 1L, "faviconUpload", "favicon.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
        Assert.Contains("1 MB or smaller", ProductFileService.ValidateFaviconAsset(oversized), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_digital_products_public_head_uses_the_configured_favicon_contract()
    {
        var digitalPublic = RepositoryDirectory("src", "BTCPayServer.Plugins.MakePay.DigitalProducts", "Views", "DigitalDownloads", "Public");
        var theme = File.ReadAllText(Path.Combine(digitalPublic, "_Theme.cshtml"));
        Assert.Contains("SafePublicResourceUrl(Model.FaviconUrl)", theme, StringComparison.Ordinal);
        Assert.Contains("if (faviconUrl is not null)", theme, StringComparison.Ordinal);
        Assert.Contains("rel=\"icon\"", theme, StringComparison.Ordinal);

        foreach (var view in Directory.GetFiles(digitalPublic, "*.cshtml"))
        {
            var source = File.ReadAllText(view);
            if (!source.Contains("<head", StringComparison.OrdinalIgnoreCase)) continue;
            Assert.Contains("_Theme.cshtml", source, StringComparison.Ordinal);
        }

        var licensePublic = RepositoryDirectory("src", "BTCPayServer.Plugins.MakePay.DigitalProducts", "Views", "LicenseManager", "Public");
        foreach (var view in Directory.GetFiles(licensePublic, "*.cshtml"))
        {
            var source = File.ReadAllText(view);
            Assert.Contains("SafePublicResourceUrl(Model.Settings.FaviconUrl)", source, StringComparison.Ordinal);
            Assert.Contains("if (faviconUrl is not null)", source, StringComparison.Ordinal);
            Assert.Contains("rel=\"icon\"", source, StringComparison.Ordinal);
        }
    }

    private static FormFile Upload(byte[] contents, string contentType)
    {
        var stream = new MemoryStream(contents, writable: false);
        return new FormFile(stream, 0, stream.Length, "faviconUpload", "favicon.bin")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static string RepositoryDirectory(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException($"Could not locate repository directory {Path.Combine(segments)}.");
    }
}
