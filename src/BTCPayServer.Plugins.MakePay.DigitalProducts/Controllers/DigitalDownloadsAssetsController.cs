#nullable enable
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Controllers;

public abstract class DigitalDownloadsAssetsControllerBase : Controller
{
    private static readonly IReadOnlyDictionary<string, (string Resource, string ContentType)> Assets =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["makepay-analytics.js"] =
                ("BTCPayServer.Plugins.MakePay.DigitalProducts.Assets.makepay-analytics.js", "text/javascript; charset=utf-8"),
            ["makepay-cart.js"] =
                ("BTCPayServer.Plugins.MakePay.DigitalProducts.Assets.makepay-cart.js", "text/javascript; charset=utf-8"),
            ["makepay-media.js"] =
                ("BTCPayServer.Plugins.MakePay.DigitalProducts.Assets.pdfjs.makepay-media.js", "text/javascript; charset=utf-8"),
            ["pdf.min.mjs"] =
                ("BTCPayServer.Plugins.MakePay.DigitalProducts.Assets.pdfjs.pdf.min.mjs", "text/javascript; charset=utf-8"),
            ["pdf.worker.min.mjs"] =
                ("BTCPayServer.Plugins.MakePay.DigitalProducts.Assets.pdfjs.pdf.worker.min.mjs", "text/javascript; charset=utf-8")
        };

    [HttpGet("{fileName}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult Asset(string fileName)
    {
        if (!Assets.TryGetValue(fileName, out var asset)) return NotFound();
        var stream = typeof(DigitalProductsPlugin).Assembly.GetManifestResourceStream(asset.Resource);
        if (stream is null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(stream, asset.ContentType);
    }
}

[Route("stores/{storeId}/downloads/assets/runtime")]
public sealed class DigitalDownloadsAssetsController(
    DigitalProductsAppService digitalApps,
    DigitalPublicUrlService publicUrls) : DigitalDownloadsAssetsControllerBase
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var storeId = context.RouteData.Values["storeId"] as string ?? "";
        var (app, domain) = await digitalApps.MappingForStore(storeId);
        if (!Request.IsOnion())
        {
            DigitalPublicUrlService.SetMapping(HttpContext, storeId, domain);
            if (app is not null) HttpContext.SetAppData(app);
        }
        if (domain is not null && !Request.IsOnion())
        {
            var redirect = DigitalPublicUrlService.CleanUrlFromLegacy(
                await publicUrls.MappedBaseUrl(storeId), storeId, Request.PathBase, Request.Path, Request.QueryString);
            if (redirect is not null)
            {
                context.Result = new RedirectResult(redirect, permanent: true, preserveMethod: true);
                return;
            }
        }
        await next();
    }
}

[Route("downloads/assets/runtime")]
[DomainMappingConstraint(DigitalProductsAppType.AppType)]
public sealed class CleanDigitalDownloadsAssetsController(DigitalProductsAppService digitalApps)
    : DigitalDownloadsAssetsControllerBase
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var appId = RouteData.Values["appId"] as string;
        var app = string.IsNullOrWhiteSpace(appId) ? null : await digitalApps.Get(appId);
        var domain = app is null ? null : digitalApps.MappedDomain(app, Request.Host.Host);
        if (app is null || domain is null)
        {
            context.Result = NotFound();
            return;
        }
        context.RouteData.Values["storeId"] = app.StoreDataId;
        DigitalPublicUrlService.SetMapping(HttpContext, app.StoreDataId, domain);
        HttpContext.SetAppData(app);
        await next();
    }
}
