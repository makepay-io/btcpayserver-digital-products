#nullable enable
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class LiveEditorUxTests
{
    private static readonly string SettingsSource = File.ReadAllText(CustomDomainGuideTests.RepositoryFile(
        "src",
        "BTCPayServer.Plugins.MakePay.DigitalProducts",
        "Views",
        "DigitalDownloads",
        "Settings.cshtml"));

    [Fact]
    public void Live_editor_shell_uses_btcpay_theme_tokens_and_primary_actions()
    {
        Assert.Contains("--builder-accent:var(--btcpay-primary", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("--builder-bg:var(--btcpay-body-bg", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("--builder-surface:var(--btcpay-bg-tile", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("--builder-text:var(--btcpay-body-text", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("--builder-border:var(--btcpay-body-border-light", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("background:var(--builder-accent);color:var(--builder-accent-text)", SettingsSource, StringComparison.Ordinal);
        Assert.Contains(".dp-editor-dialog :focus-visible", SettingsSource, StringComparison.Ordinal);

        Assert.DoesNotContain("--builder-blue:#155eef", SettingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("--builder-warm:#f5f3ef", SettingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Live_editor_uploads_use_accessible_custom_picker_and_keep_real_file_inputs()
    {
        Assert.Contains("const uploadField=", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("class=\"dp-editor-upload-picker\"", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("class=\"dp-editor-upload-input\"", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("type=\"file\"", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"${escapeAttribute(title)}\"", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("data-upload-logo", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("data-upload-favicon", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("data-upload-slide", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("const stashUpload=", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("data-upload-field-name", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("target.name=fieldName", SettingsSource, StringComparison.Ordinal);
        Assert.Contains("uploadBin.appendChild(target)", SettingsSource, StringComparison.Ordinal);
    }
}
