#nullable enable
using System.Reflection;
using System.Runtime.CompilerServices;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using BTCPayServer.Plugins.MakePay.DigitalProducts.TagHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class CustomDomainRoutingTests
{
    private const string StoreId = "store-1";

    [Theory]
    [InlineData("/stores/store-1/downloads", "/downloads")]
    [InlineData("/stores/store-1/downloads/cart?currency=USD#total", "/downloads/cart?currency=USD#total")]
    [InlineData("/stores/store-1/downloads/products/download/guide", "/downloads/products/download/guide")]
    [InlineData("/stores/store-1/downloads/products/product-1/preview/sample-1", "/downloads/products/product-1/preview/sample-1")]
    [InlineData("/stores/store-1/downloads/order/order-1/file?token=secret", "/downloads/order/order-1/file?token=secret")]
    [InlineData("/stores/store-1/downloads/order/order-1/stream?token=secret", "/downloads/order/order-1/stream?token=secret")]
    [InlineData("/stores/store-1/downloads/assets/runtime/makepay-media.js", "/downloads/assets/runtime/makepay-media.js")]
    public void Legacy_public_surface_rewrites_to_clean_paths(string legacy, string expected)
    {
        Assert.Equal(expected, DigitalPublicUrlService.RewriteLegacyUrl(StoreId, legacy));
    }

    [Theory]
    [InlineData("/btcpay/stores/store-1/downloads/cart", "/btcpay/downloads/cart")]
    [InlineData("/stores/store-1/downloads/cart", "/btcpay/downloads/cart")]
    public void Legacy_paths_preserve_the_configured_path_base(string legacy, string expected)
    {
        Assert.Equal(expected,
            DigitalPublicUrlService.RewriteLegacyUrl(StoreId, legacy, new PathString("/btcpay")));
    }

    [Theory]
    [InlineData("/stores/store-10/downloads", "/stores/store-10/downloads")]
    [InlineData("/stores/store-1/downloads-extra", "/stores/store-1/downloads-extra")]
    [InlineData("https://elsewhere.example/stores/store-1/downloads", "https://elsewhere.example/stores/store-1/downloads")]
    public void Rewriter_does_not_touch_colliding_or_absolute_values(string value, string expected)
    {
        Assert.Equal(expected, DigitalPublicUrlService.RewriteLegacyUrl(StoreId, value));
    }

    [Fact]
    public void Store_route_value_is_removed_only_from_clean_plugin_urls()
    {
        Assert.Equal("/downloads/cart?currency=USD#summary",
            DigitalPublicUrlService.RemoveStoreIdFromCleanUrl(
                "/downloads/cart?storeId=store-1&currency=USD#summary", StoreId));
        Assert.Equal("/downloads/cart?storeId=another-store",
            DigitalPublicUrlService.RemoveStoreIdFromCleanUrl(
                "/downloads/cart?storeId=another-store", StoreId));
        Assert.Equal("/outside?storeId=store-1",
            DigitalPublicUrlService.RemoveStoreIdFromCleanUrl(
                "/outside?storeId=store-1", StoreId));
    }

    [Fact]
    public void Legacy_canonical_url_uses_mapped_origin_once_with_path_base()
    {
        var result = DigitalPublicUrlService.CleanUrlFromLegacy(
            "https://shop.example/btcpay",
            StoreId,
            new PathString("/btcpay"),
            new PathString("/stores/store-1/downloads/cart"),
            new QueryString("?currency=USD"));

        Assert.Equal("https://shop.example/btcpay/downloads/cart?currency=USD", result);
        Assert.Null(DigitalPublicUrlService.CleanUrlFromLegacy(
            null, StoreId, PathString.Empty, new PathString("/stores/store-1/downloads"), QueryString.Empty));
    }

    [Theory]
    [InlineData("SHOP.Example.COM", "shop.example.com")]
    [InlineData("münich.example", "xn--mnich-kva.example")]
    public void Hostnames_are_normalized_without_ports(string value, string expected)
    {
        Assert.True(DigitalPublicUrlService.TryNormalizeHostname(value, out var hostname, out _));
        Assert.Equal(expected, hostname);
        Assert.Equal(expected, DigitalPublicUrlService.NormalizeRequestHost(new HostString(value, 443)));
    }

    [Theory]
    [InlineData("example")]
    [InlineData("https://shop.example")]
    [InlineData("*.example.com")]
    [InlineData("shop.example.com/path")]
    public void Invalid_mapped_hosts_are_rejected(string value)
    {
        Assert.False(DigitalPublicUrlService.TryNormalizeHostname(value, out _, out _));
    }

    [Theory]
    [InlineData("SHOP.Example.COM", true, "shop.example.com")]
    [InlineData("xn--mnich-kva.example", true, "xn--mnich-kva.example")]
    [InlineData("münich.example", false, "")]
    [InlineData("shop.example.com.", false, "")]
    [InlineData(" shop.example.com", false, "")]
    public void Native_policy_domains_must_already_match_normalized_ascii(
        string rawValue, bool expected, string expectedHostname)
    {
        Assert.Equal(expected,
            DigitalPublicUrlService.TryGetNativePolicyHostname(rawValue, out var hostname));
        Assert.Equal(expectedHostname, hostname);
    }

    [Fact]
    public void Native_duplicate_precedence_matches_the_first_global_exact_domain_row()
    {
        string?[] domains = ["other.example", "SHOP.example", "shop.example", "shop.example.", null];

        Assert.True(DigitalPublicUrlService.IsFirstExactDomain(domains, 0));
        Assert.True(DigitalPublicUrlService.IsFirstExactDomain(domains, 1));
        Assert.False(DigitalPublicUrlService.IsFirstExactDomain(domains, 2));
        Assert.True(DigitalPublicUrlService.IsFirstExactDomain(domains, 3));
        Assert.False(DigitalPublicUrlService.IsFirstExactDomain(domains, 4));
    }

    [Fact]
    public void Onion_origins_and_legacy_paths_are_preserved_with_root_path()
    {
        const string onion = "http://abcdefghijklmnopqrstuvwxyz234567abcdefghijklmnopqrstuvwx.onion/btcpay";
        var legacyPath = DigitalPublicUrlService.LegacyPrefix(StoreId) + "/account";

        Assert.True(DigitalPublicUrlService.IsOnionBaseUrl(onion));
        Assert.False(DigitalPublicUrlService.IsOnionBaseUrl("https://shop.example/btcpay"));
        Assert.Equal(onion, DigitalPublicUrlService.BuildLegacyOrigin(onion, "/btcpay"));
        Assert.Equal(
            $"http://abcdefghijklmnopqrstuvwxyz234567abcdefghijklmnopqrstuvwx.onion/btcpay{legacyPath}",
            DigitalPublicUrlService.BuildLegacyAbsolute(onion, legacyPath, "/btcpay"));
        Assert.Equal($"/btcpay{legacyPath}", DigitalPublicUrlService.AddRootPath(legacyPath, "/btcpay"));
        Assert.Equal($"/btcpay{legacyPath}",
            DigitalPublicUrlService.AddRootPath($"/btcpay{legacyPath}", "/btcpay"));
    }

    [Fact]
    public void Request_rewriting_uses_native_mapping_context_and_not_arbitrary_host_state()
    {
        var service = CreatePublicUrlService();
        var context = CleanContext("shop.example", "/btcpay");

        Assert.Equal("/btcpay/downloads/cart", service.ForRequest(context, StoreId,
            "/btcpay/stores/store-1/downloads/cart?storeId=store-1"));

        context.Request.Host = new HostString("pay.example");
        Assert.Equal("https://shop.example/btcpay/downloads/cart", service.ForRequest(context, StoreId,
            "/btcpay/stores/store-1/downloads/cart"));

        var unmapped = new DefaultHttpContext();
        Assert.Equal("/stores/store-1/downloads/cart",
            service.ForRequest(unmapped, StoreId, "/stores/store-1/downloads/cart"));

        var onion = CleanContext(
            "abcdefghijklmnopqrstuvwxyz234567abcdefghijklmnopqrstuvwx.onion", "/btcpay");
        Assert.Equal("/btcpay/stores/store-1/downloads/account",
            service.ForRequest(onion, StoreId, "/btcpay/stores/store-1/downloads/account"));
    }

    [Fact]
    public void Both_controllers_expose_the_complete_public_action_surface()
    {
        var source = Source("Controllers", "DigitalDownloadsPublicController.cs");

        Assert.Contains("[Route(\"stores/{storeId}/downloads\")]", source, StringComparison.Ordinal);
        Assert.Contains("[Route(\"downloads\")]", source, StringComparison.Ordinal);
        Assert.Contains("[DomainMappingConstraint(DigitalProductsAppType.AppType)]", source, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"/\")]", source, StringComparison.Ordinal);
        Assert.Contains("Redirect(Url.Content(\"~/downloads\"))", source, StringComparison.Ordinal);

        string[] required =
        [
            "", "assets/hero", "assets/product/{kind}", "products/{productId}/preview/{assetId}",
            "assets/storefront/{assetId}/{fileName}", "products/{kind}/{productId}", "cart/items", "cart",
            "cart/update", "login", "login/request", "login/verify", "logout", "checkout",
            "checkout/{checkoutId}/payment", "checkout/{checkoutId}/status", "purchase/{checkoutId}",
            "account", "buy/{productId}", "order/{orderId}", "order/{orderId}/file", "order/{orderId}/stream"
        ];

        Assert.All(required, template =>
            Assert.Contains($"(\"{template}\")", source, StringComparison.Ordinal));
    }

    [Fact]
    public void Runtime_assets_have_legacy_and_native_clean_routes()
    {
        var source = Source("Controllers", "DigitalDownloadsAssetsController.cs");

        Assert.Contains("[Route(\"stores/{storeId}/downloads/assets/runtime\")]", source, StringComparison.Ordinal);
        Assert.Contains("[Route(\"downloads/assets/runtime\")]", source, StringComparison.Ordinal);
        Assert.Contains("[DomainMappingConstraint(DigitalProductsAppType.AppType)]", source, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"{fileName}\")]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_app_registration_and_tenant_derivation_are_explicit()
    {
        var plugin = Source("DigitalProductsPlugin.cs");
        var appService = Source("Services", "DigitalProductsAppService.cs");
        var publicController = Source("Controllers", "DigitalDownloadsPublicController.cs");
        var assetsController = Source("Controllers", "DigitalDownloadsAssetsController.cs");
        var adminController = Source("Controllers", "DigitalDownloadsAdminController.cs");

        Assert.Contains("AddSingleton<AppBaseType, DigitalProductsAppType>()", plugin, StringComparison.Ordinal);
        Assert.Contains("Description = \"MakePay Digital Products storefront", appService, StringComparison.Ordinal);
        Assert.Contains("DigitalPublicUrlService.LegacyController", appService, StringComparison.Ordinal);
        Assert.Contains("GetApp(appId, DigitalProductsAppType.AppType, includeStore: true)", appService, StringComparison.Ordinal);
        Assert.Contains("app is { Archived: false }", appService, StringComparison.Ordinal);
        Assert.Contains("policies.DomainToAppMapping", appService, StringComparison.Ordinal);
        Assert.Contains("IsFirstExactDomain(domains, index)", appService, StringComparison.Ordinal);
        Assert.Contains("TryGetNativePolicyHostname(mappings[index].Domain", appService, StringComparison.Ordinal);
        Assert.Contains("StringComparison.Ordinal) &&", appService, StringComparison.Ordinal);
        Assert.Contains("string.Equals(mapping.Item.Domain, requestHost", appService, StringComparison.Ordinal);
        Assert.Contains("StringComparison.InvariantCultureIgnoreCase", appService, StringComparison.Ordinal);
        Assert.Contains("if (IsOnionBaseUrl(legacyBaseUrl)) return rootedLegacyPath;", appService,
            StringComparison.Ordinal);
        Assert.Contains("if (IsOnionBaseUrl(legacyBaseUrl))\n            return BuildLegacyAbsolute", appService,
            StringComparison.Ordinal);
        Assert.Contains("if (IsOnionBaseUrl(legacyBaseUrl)) return BuildLegacyOrigin", appService,
            StringComparison.Ordinal);
        Assert.Contains("context.ActionArguments[\"storeId\"] = app.StoreDataId", publicController, StringComparison.Ordinal);
        Assert.Contains("context.RouteData.Values[\"storeId\"] = app.StoreDataId", publicController, StringComparison.Ordinal);
        Assert.Contains("context.RouteData.Values[\"storeId\"] = app.StoreDataId", assetsController, StringComparison.Ordinal);
        Assert.Contains("HttpMethods.IsGet(Request.Method) || HttpMethods.IsHead(Request.Method)", publicController, StringComparison.Ordinal);
        Assert.Contains("domain is not null && !Request.IsOnion()", publicController, StringComparison.Ordinal);
        Assert.Contains("domain is not null && !Request.IsOnion()", assetsController, StringComparison.Ordinal);
        Assert.Contains("if (!Request.IsOnion())\n        {\n            DigitalPublicUrlService.SetMapping", publicController, StringComparison.Ordinal);
        Assert.Contains("if (!Request.IsOnion())\n        {\n            DigitalPublicUrlService.SetMapping", assetsController, StringComparison.Ordinal);
        Assert.Contains("if (app is not null) HttpContext.SetAppData(app);", publicController, StringComparison.Ordinal);
        Assert.Contains("if (app is not null) HttpContext.SetAppData(app);", assetsController, StringComparison.Ordinal);
        Assert.Contains("digitalApps.MappedDomain(app, Request.Host.Host)", publicController, StringComparison.Ordinal);
        Assert.Contains("digitalApps.MappedDomain(app, Request.Host.Host)", assetsController, StringComparison.Ordinal);
        Assert.Contains("if (app is null || domain is null)", publicController, StringComparison.Ordinal);
        Assert.Contains("if (app is null || domain is null)", assetsController, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(publicController, "OrderUrl = successUrl"));
        Assert.DoesNotContain("OrderUrl = Request.GetDisplayUrl()", publicController, StringComparison.Ordinal);
        Assert.Contains("CreateApp", adminController, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateOrCreateApp", adminController, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureForStore", adminController, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }

        return count;
    }

    [Fact]
    public void Implementation_has_no_parallel_custom_domain_registry_or_middleware()
    {
        var sourceRoot = Path.GetDirectoryName(CustomDomainGuideTests.RepositoryFile(
            "src", "BTCPayServer.Plugins.MakePay.DigitalProducts", "DigitalProductsPlugin.cs"))!;
        var source = string.Join('\n', Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        Assert.DoesNotContain("CleanDomainHostname", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CustomDomainHostname", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DigitalCustomDomain", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CleanDomainMiddleware", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DomainRegistry", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Background_delivery_links_are_centralized_through_the_mapping_service()
    {
        var checkout = Source("Services", "DigitalCheckoutFulfillmentService.cs");
        var delivery = Source("Services", "DigitalDeliveryService.cs");

        Assert.Contains("publicUrls.Absolute", checkout, StringComparison.Ordinal);
        Assert.Contains("publicUrls.CanonicalPath", checkout, StringComparison.Ordinal);
        Assert.Contains("checkout.StoreId, checkout.PublicBaseUrl, legacyPrefix + \"/account\"", checkout,
            StringComparison.Ordinal);
        Assert.Contains("publicUrls.Absolute", delivery, StringComparison.Ordinal);
        Assert.DoesNotContain("$\"{root}/stores", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("$\"{root}/stores", delivery, StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_rewriter_runs_after_mvc_and_targets_generated_links_forms_and_media()
    {
        var targets = typeof(DigitalCleanDomainUrlTagHelper)
            .GetCustomAttributes<HtmlTargetElementAttribute>()
            .ToDictionary(attribute => attribute.Tag, StringComparer.OrdinalIgnoreCase);

        Assert.Null(targets["a"].Attributes);
        Assert.Null(targets["form"].Attributes);
        Assert.All(new[] { "img", "script", "source", "link", "video", "audio" },
            tag => Assert.Contains(tag, targets.Keys));

        var helper = CreateCleanTagHelper(out var viewContext);
        Assert.Equal(int.MaxValue, helper.Order);

        var anchorOutput = MvcGeneratedOutput("a", "href", "/btcpay/stores/store-1/downloads/products/download/guide");
        helper.Process(TagContext(), anchorOutput);
        Assert.Equal("/btcpay/downloads/products/download/guide", anchorOutput.Attributes["href"].Value);

        var formOutput = MvcGeneratedOutput("form", "action", "/btcpay/stores/store-1/downloads/cart/items");
        helper.ViewContext = viewContext;
        helper.Process(TagContext(), formOutput);
        Assert.Equal("/btcpay/downloads/cart/items", formOutput.Attributes["action"].Value);

        var sourceOutput = MvcGeneratedOutput("source", "src", "/btcpay/stores/store-1/downloads/order/o/stream?token=t");
        helper.Process(TagContext(), sourceOutput);
        Assert.Equal("/btcpay/downloads/order/o/stream?token=t", sourceOutput.Attributes["src"].Value);
    }

    [Fact]
    public void Mvc_generated_asp_action_anchor_and_form_are_cleaned_after_generation()
    {
        var generator = DispatchProxy.Create<IHtmlGenerator, GeneratedUrlHtmlGenerator>();
        var proxy = (GeneratedUrlHtmlGenerator)(object)generator;
        var helper = CreateCleanTagHelper(out var viewContext);

        proxy.GeneratedUrl = "/btcpay/stores/store-1/downloads/cart";
        var anchorContext = TagContext(new TagHelperAttribute("asp-action", "CartPage"));
        var anchorOutput = EmptyOutput("a");
        var anchor = new AnchorTagHelper(generator)
        {
            ViewContext = viewContext,
            Action = "CartPage",
            Controller = DigitalPublicUrlService.LegacyController
        };
        anchor.RouteValues["storeId"] = StoreId;
        anchor.Process(anchorContext, anchorOutput);
        helper.Process(anchorContext, anchorOutput);
        Assert.Equal("/btcpay/downloads/cart", anchorOutput.Attributes["href"].Value);
        Assert.DoesNotContain("/stores/store-1", anchorOutput.Attributes["href"].Value!.ToString(), StringComparison.Ordinal);

        proxy.GeneratedUrl = "/btcpay/stores/store-1/downloads/cart/items";
        var formContext = TagContext(
            new TagHelperAttribute("asp-action", "AddToCart"),
            new TagHelperAttribute("method", "post"));
        var formOutput = EmptyOutput("form");
        var form = new FormTagHelper(generator)
        {
            ViewContext = viewContext,
            Action = "AddToCart",
            Controller = DigitalPublicUrlService.LegacyController,
            Method = "post",
            Antiforgery = false
        };
        form.RouteValues["storeId"] = StoreId;
        form.Process(formContext, formOutput);
        helper.Process(formContext, formOutput);
        Assert.Equal("/btcpay/downloads/cart/items", formOutput.Attributes["action"].Value);
        Assert.DoesNotContain("/stores/store-1", formOutput.Attributes["action"].Value!.ToString(), StringComparison.Ordinal);
    }

    private static DigitalPublicUrlService CreatePublicUrlService() =>
        (DigitalPublicUrlService)RuntimeHelpers.GetUninitializedObject(typeof(DigitalPublicUrlService));

    private static DefaultHttpContext CleanContext(string hostname, string pathBase)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(hostname);
        context.Request.PathBase = new PathString(pathBase);
        DigitalPublicUrlService.SetMapping(context, StoreId, "shop.example");
        return context;
    }

    private static DigitalCleanDomainUrlTagHelper CreateCleanTagHelper(out ViewContext viewContext)
    {
        var context = CleanContext("shop.example", "/btcpay");
        viewContext = new ViewContext { HttpContext = context };
        return new DigitalCleanDomainUrlTagHelper(CreatePublicUrlService()) { ViewContext = viewContext };
    }

    private static TagHelperContext TagContext(params TagHelperAttribute[] attributes) =>
        new(new TagHelperAttributeList(attributes), new Dictionary<object, object>(), Guid.NewGuid().ToString("N"));

    private static TagHelperOutput EmptyOutput(string tag) =>
        new(tag, new TagHelperAttributeList(), (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

    private static TagHelperOutput MvcGeneratedOutput(string tag, string attribute, string value)
    {
        var output = EmptyOutput(tag);
        output.Attributes.SetAttribute(attribute, value);
        return output;
    }

    private static string Source(params string[] segments) => File.ReadAllText(
        CustomDomainGuideTests.RepositoryFile(["src", "BTCPayServer.Plugins.MakePay.DigitalProducts", .. segments]));

    public class GeneratedUrlHtmlGenerator : DispatchProxy
    {
        public string GeneratedUrl { get; set; } = "/";

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name is nameof(IHtmlGenerator.GenerateActionLink))
                return Tag("a", "href");
            if (targetMethod?.Name is nameof(IHtmlGenerator.GenerateForm))
                return Tag("form", "action");
            if (targetMethod?.ReturnType == typeof(string)) return "";
            if (targetMethod?.ReturnType == typeof(bool)) return false;
            if (targetMethod?.ReturnType.IsValueType == true) return Activator.CreateInstance(targetMethod.ReturnType);
            return null;
        }

        private TagBuilder Tag(string name, string attribute)
        {
            var tag = new TagBuilder(name);
            tag.Attributes[attribute] = GeneratedUrl;
            return tag;
        }
    }
}
