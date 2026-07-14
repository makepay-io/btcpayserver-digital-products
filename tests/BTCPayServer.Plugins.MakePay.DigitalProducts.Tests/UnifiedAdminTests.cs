using System.Reflection;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Controllers;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.LicenseManager.Controllers;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class UnifiedAdminTests
{
    [Fact]
    public void Legacy_license_dashboard_redirects_to_the_unified_license_tab()
    {
        var result = Assert.IsType<RedirectToActionResult>(
            new LegacyLicenseManagerAdminController().Index("store-1"));

        Assert.Equal(nameof(DigitalDownloadsAdminController.Index), result.ActionName);
        Assert.Equal("DigitalDownloadsAdmin", result.ControllerName);
        Assert.Equal("store-1", result.RouteValues!["storeId"]);
        Assert.Equal("licenses", result.RouteValues["section"]);
    }

    [Fact]
    public void Legacy_license_editor_bookmarks_redirect_to_canonical_digital_download_routes()
    {
        var controller = new LegacyLicenseManagerAdminController();

        var product = Assert.IsType<RedirectToActionResult>(controller.Product("store-1", "license-1"));
        Assert.Equal(nameof(LicenseManagerAdminController.Product), product.ActionName);
        Assert.Equal("LicenseManagerAdmin", product.ControllerName);
        Assert.Equal("license-1", product.RouteValues!["productId"]);

        var settings = Assert.IsType<RedirectToActionResult>(controller.Settings("store-1"));
        Assert.Equal(nameof(LicenseManagerAdminController.Settings), settings.ActionName);
        Assert.Equal("LicenseManagerAdmin", settings.ControllerName);
    }

    [Fact]
    public void License_admin_uses_digital_downloads_as_its_canonical_route()
    {
        var route = Assert.Single(typeof(LicenseManagerAdminController).GetCustomAttributes<RouteAttribute>());
        Assert.Equal("plugins/{storeId}/digital-downloads/licenses", route.Template);
    }

    [Theory]
    [InlineData(nameof(LicenseManagerAdminController.SaveProduct), "~/plugins/{storeId}/license-manager/products/{productId}")]
    [InlineData(nameof(LicenseManagerAdminController.SaveSettings), "~/plugins/{storeId}/license-manager/settings")]
    [InlineData(nameof(LicenseManagerAdminController.GenerateSecret), "~/plugins/{storeId}/license-manager/settings/generate-secret")]
    [InlineData(nameof(LicenseManagerAdminController.Issue), "~/plugins/{storeId}/license-manager/licenses/issue")]
    [InlineData(nameof(LicenseManagerAdminController.Status), "~/plugins/{storeId}/license-manager/licenses/{licenseId}/status")]
    public void Legacy_admin_posts_remain_accepted(string methodName, string legacyTemplate)
    {
        var method = typeof(LicenseManagerAdminController).GetMethod(methodName)!;
        var postTemplates = method.GetCustomAttributes<HttpPostAttribute>().Select(attribute => attribute.Template).ToArray();

        Assert.Contains(legacyTemplate, postTemplates);
    }

    [Fact]
    public void Digital_dashboard_renders_license_management_in_the_same_view()
    {
        var view = Source("Views", "DigitalDownloads", "Index.cshtml");

        Assert.Contains("asp-route-section=\"licenses\"", view, StringComparison.Ordinal);
        Assert.Contains("Model.LicenseProducts", view, StringComparison.Ordinal);
        Assert.Contains("Issue a license manually", view, StringComparison.Ordinal);
        Assert.Contains("Issued licenses", view, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-controller=\"LicenseManagerAdmin\" asp-action=\"Index\"", view, StringComparison.Ordinal);
        var digitalIndex = Path("Views", "DigitalDownloads", "Index.cshtml");
        var viewsDirectory = Directory.GetParent(digitalIndex)!.Parent!.FullName;
        Assert.False(File.Exists(System.IO.Path.Combine(viewsDirectory, "LicenseManager", "Index.cshtml")));
    }

    [Theory]
    [InlineData("Views", "DigitalDownloads", "Product.cshtml")]
    [InlineData("Views", "LicenseManager", "Product.cshtml")]
    public void Product_editors_render_validation_alerts_only_when_errors_exist(params string[] segments)
    {
        var source = File.ReadAllText(Path(segments));

        Assert.Contains("ViewData.ModelState.ErrorCount > 0", source, StringComparison.Ordinal);
        Assert.Contains("asp-validation-summary=\"All\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ManagedLicenseStatus.Suspended)]
    [InlineData(ManagedLicenseStatus.Revoked)]
    [InlineData(ManagedLicenseStatus.Expired)]
    public void License_status_binding_preserves_every_non_default_status(ManagedLicenseStatus requested)
    {
        Assert.True(LicenseManagerAdminController.TryNormalizeStatus(requested, out var normalized));
        Assert.Equal(requested, normalized);
    }

    [Fact]
    public void License_status_binding_rejects_missing_or_undefined_values()
    {
        Assert.False(LicenseManagerAdminController.TryNormalizeStatus(null, out _));
        Assert.False(LicenseManagerAdminController.TryNormalizeStatus((ManagedLicenseStatus)999, out _));

        var parameter = typeof(LicenseManagerAdminController).GetMethod(nameof(LicenseManagerAdminController.Status))!
            .GetParameters().Single(item => item.Name == "status");
        Assert.Equal(typeof(ManagedLicenseStatus?), parameter.ParameterType);
    }

    [Fact]
    public void Hero_upload_selection_skips_an_empty_same_name_file()
    {
        const string fieldName = "heroSlideUpload_slide1";
        var empty = new FormFile(Stream.Null, 0, 0, fieldName, string.Empty);
        var bytes = new byte[] { 1, 2, 3 };
        var selected = new FormFile(new MemoryStream(bytes), 0, bytes.Length, fieldName, "hero.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
        var files = new FormFileCollection { empty, selected };

        var result = DigitalDownloadsAdminController.NonEmptyUpload(files, fieldName);

        Assert.Same(selected, result);
    }

    private static string Source(params string[] segments) => File.ReadAllText(Path(segments));

    private static string Path(params string[] segments) =>
        CustomDomainGuideTests.RepositoryFile(["src", "BTCPayServer.Plugins.MakePay.DigitalProducts", .. segments]);
}
