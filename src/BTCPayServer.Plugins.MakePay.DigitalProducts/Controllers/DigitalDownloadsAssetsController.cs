#nullable enable
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Controllers;

[Route("stores/{storeId}/downloads/assets/runtime")]
public sealed class DigitalDownloadsAssetsController : Controller
{
    private static readonly IReadOnlyDictionary<string, (string Resource, string ContentType)> Assets =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["makepay-analytics.js"] =
                ("BTCPayServer.Plugins.MakePay.DigitalProducts.Assets.makepay-analytics.js", "text/javascript; charset=utf-8"),
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
