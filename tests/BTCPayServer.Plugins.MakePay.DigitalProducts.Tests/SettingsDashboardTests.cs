#nullable enable
using System.Text.RegularExpressions;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class SettingsDashboardTests
{
    private static readonly string SettingsSource = File.ReadAllText(CustomDomainGuideTests.RepositoryFile(
        "src",
        "BTCPayServer.Plugins.MakePay.DigitalProducts",
        "Views",
        "DigitalDownloads",
        "Settings.cshtml"));

    [Fact]
    public void Settings_use_accessible_dashboard_tabs_and_error_aware_navigation()
    {
        Assert.Contains("class=\"mp-settings-dashboard\"", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("role=\"tablist\"", SettingsSource, StringComparison.Ordinal);
        Assert.Equal(5, Regex.Matches(SettingsSource, "role=\\\"tab\\\"").Count);
        Assert.Equal(5, Regex.Matches(SettingsSource, "role=\\\"tabpanel\\\"").Count);

        foreach (var panel in new[] { "storefront", "customer", "analytics", "delivery", "domain" })
        {
            Assert.Contains($"data-settings-tab=\"{panel}\"", SettingsSource, StringComparison.Ordinal);
            Assert.Contains($"data-settings-panel=\"{panel}\"", SettingsSource, StringComparison.Ordinal);
            Assert.Contains($"aria-controls=\"settings-panel-{panel}\"", SettingsSource, StringComparison.Ordinal);
            Assert.Contains($"aria-labelledby=\"settings-tab-{panel}\"", SettingsSource, StringComparison.Ordinal);
        }

        Assert.Contains("event.key==='ArrowRight'", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("event.key==='ArrowLeft'", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("event.key==='Home'", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("event.key==='End'", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("invalid?.closest('[data-settings-panel]')", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("form=\"dp-settings-form\" data-settings-submit", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("id=\"dp-settings-submit\" data-settings-submit", SettingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_preserve_binding_editor_and_write_only_secret_contracts()
    {
        foreach (var field in new[]
                 {
                     "StorefrontTitle", "StorefrontAnnouncement", "StorefrontDescription", "LogoUrl", "FaviconUrl",
                     "CatalogTitle", "CatalogSubtitle", "FontStyle", "Currency", "LoginCodeMinutes",
                     "CustomerSessionHours", "LibraryTitle", "ConfirmationTitle", "LoginEmailHtml", "PurchaseEmailHtml",
                     "AnalyticsProvider", "GoogleTagManagerContainerId", "GoogleAnalyticsMeasurementId",
                     "DefaultDownloadLimit", "DefaultLinkHours", "S3Endpoint", "S3Region", "S3Bucket", "S3AccessKey",
                     "RemoteAuthorizationHeader", "EmailDeliveryEnabled", "EmailSubject", "EmailHtml"
                 })
        {
            Assert.Single(Regex.Matches(SettingsSource, $"<(?:input|select|textarea)\\s+asp-for=\\\"{field}\\\"").Cast<Match>());
        }

        Assert.Contains("method=\"post\" enctype=\"multipart/form-data\"", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"SaveSettings\"", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("name=\"heroSlidesJson\" id=\"heroSlidesJson\"", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("name=\"categoriesJson\" id=\"categoriesJson\"", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("type=\"password\" name=\"s3SecretKey\"", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("type=\"password\" name=\"remoteAuthorizationValue\"", SettingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"PromotionText\"", SettingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"ShowMakePayPromotion\"", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("id=\"open-dp-editor\"", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("dialog.showModal()", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("form.requestSubmit()", SettingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_editor_mode_controls_are_unique()
    {
        var modes = Regex.Matches(SettingsSource, "data-editor-mode=\\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(modes);
        Assert.Equal(modes.Length, modes.Distinct(StringComparer.Ordinal).Count());
    }
}
