#nullable enable
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.TagHelpers;

/// <summary>
/// Runs after MVC anchor/form tag helpers and converts every plugin-owned local
/// URL-bearing attribute to its clean equivalent during a clean-domain request.
/// It only removes the server-known current store prefix and never uses Host.
/// </summary>
[HtmlTargetElement("a")]
[HtmlTargetElement("form")]
[HtmlTargetElement("img", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("script")]
[HtmlTargetElement("source", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("link", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("video")]
[HtmlTargetElement("audio")]
public sealed class DigitalCleanDomainUrlTagHelper(DigitalPublicUrlService publicUrls) : TagHelper
{
    private static readonly string[] UrlAttributes =
        ["href", "action", "src", "poster", "data-source", "data-library", "data-worker"];

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override int Order => int.MaxValue;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var httpContext = ViewContext.HttpContext;
        if (!httpContext.Items.TryGetValue(DigitalPublicUrlService.StoreItem, out var value) || value is not string storeId)
            return;

        foreach (var attributeName in UrlAttributes)
        {
            var attribute = output.Attributes[attributeName];
            if (attribute?.Value is not string url || string.IsNullOrWhiteSpace(url)) continue;
            var rewritten = publicUrls.ForRequest(httpContext, storeId, url);
            if (!string.Equals(url, rewritten, StringComparison.Ordinal))
                output.Attributes.SetAttribute(attributeName, rewritten);
        }
    }
}
