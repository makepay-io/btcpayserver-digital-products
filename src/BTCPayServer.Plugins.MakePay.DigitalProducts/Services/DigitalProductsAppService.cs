#nullable enable
using System.Globalization;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Controllers;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

/// <summary>
/// Native BTCPay application identity for the Digital Products storefront.
/// Store content remains in the plugin repository; AppData is the explicit,
/// server-administrator-controlled identity used by Policies domain mapping.
/// </summary>
public sealed class DigitalProductsAppType : AppBaseType
{
    public const string AppType = "MakePayDigitalProducts";
    private readonly LinkGenerator _links;
    private readonly IOptions<BTCPayServerOptions> _options;

    public DigitalProductsAppType(LinkGenerator links, IOptions<BTCPayServerOptions> options) : base(AppType)
    {
        _links = links;
        _options = options;
        Description = "MakePay Digital Products storefront (files, ebooks, audio, video, photos, art, and licenses)";
    }

    public override Task<object?> GetInfo(AppData appData) => Task.FromResult<object?>(null);

    public override Task<string> ConfigureLink(AppData app) => Task.FromResult(
        _links.GetPathByAction(nameof(DigitalDownloadsAdminController.Settings), "DigitalDownloadsAdmin",
            new { storeId = app.StoreDataId }, _options.Value.RootPath) ??
        $"{DigitalPublicUrlService.NormalizeRootPath(_options.Value.RootPath)}/plugins/{app.StoreDataId}/digital-downloads/settings");

    // Keep the regular BTCPay app link stable. Native Policies mapping handles
    // the optional mapped hostname; the legacy route always remains available.
    public override Task<string> ViewLink(AppData app) => Task.FromResult(
        _links.GetPathByAction(nameof(DigitalDownloadsPublicControllerBase.Storefront),
            DigitalPublicUrlService.LegacyController,
            new { storeId = app.StoreDataId }, _options.Value.RootPath) ??
        $"{DigitalPublicUrlService.NormalizeRootPath(_options.Value.RootPath)}{DigitalPublicUrlService.LegacyPrefix(app.StoreDataId)}");

    public override Task SetDefaultSettings(AppData appData, string defaultCurrency)
    {
        appData.SetSettings(new DigitalProductsAppMarker());
        return Task.CompletedTask;
    }

    public sealed class DigitalProductsAppMarker
    {
        public int SchemaVersion { get; set; } = 1;
    }
}

/// <summary>Reads explicit Digital Products AppData records and native domain mappings.</summary>
public sealed class DigitalProductsAppService(AppService apps, PoliciesSettings policies)
{
    public async Task<IReadOnlyList<AppData>> GetForStore(string storeId) =>
        (await apps.GetApps(DigitalProductsAppType.AppType))
        .Where(app => !app.Archived && app.StoreDataId.Equals(storeId, StringComparison.Ordinal))
        .OrderBy(app => app.Created)
        .ToList();

    public async Task<AppData?> Get(string appId)
    {
        var app = await apps.GetApp(appId, DigitalProductsAppType.AppType, includeStore: true);
        return app is { Archived: false } ? app : null;
    }

    public string? MappedDomain(AppData app, string? requestHost = null)
    {
        return ActiveNativeMappings()
            .Where(mapping =>
                string.Equals(mapping.Item.AppType, DigitalProductsAppType.AppType, StringComparison.Ordinal) &&
                string.Equals(mapping.Item.AppId, app.Id, StringComparison.Ordinal))
            // Match the raw request host just as DomainMappingConstraint does.
            // Normalizing here would incorrectly activate a shadow row whose
            // stored value contains a trailing dot, whitespace, or Unicode.
            .Where(mapping => requestHost is null ||
                              string.Equals(mapping.Item.Domain, requestHost,
                                  StringComparison.InvariantCultureIgnoreCase))
            .Select(mapping => mapping.Domain)
            .FirstOrDefault();
    }

    /// <summary>
    /// Finds the app selected by native Policies mapping. This intentionally
    /// inspects every app for the store instead of assuming the oldest app is
    /// the mapped one.
    /// </summary>
    public async Task<(AppData? App, string? Domain)> MappingForStore(string storeId)
    {
        var storeApps = await GetForStore(storeId);
        if (storeApps.Count == 0) return (null, null);
        var byId = storeApps.ToDictionary(app => app.Id, StringComparer.Ordinal);
        foreach (var mapping in ActiveNativeMappings())
        {
            if (!string.Equals(mapping.Item.AppType, DigitalProductsAppType.AppType, StringComparison.Ordinal) ||
                !byId.TryGetValue(mapping.Item.AppId, out var app))
                continue;
            return (app, mapping.Domain);
        }
        return (storeApps[0], null);
    }

    /// <summary>
    /// Mirrors BTCPay's native constraint: only the first global row for an
    /// exact domain can win. We additionally require the stored value to
    /// already be normalized ASCII/punycode, because the upstream constraint
    /// performs a raw case-insensitive comparison and does not normalize it.
    /// </summary>
    private IEnumerable<(PoliciesSettings.DomainToAppMappingItem Item, string Domain)> ActiveNativeMappings()
    {
        var mappings = policies.DomainToAppMapping ?? [];
        var domains = mappings.Select(mapping => (string?)mapping.Domain).ToArray();
        for (var index = 0; index < mappings.Count; index++)
        {
            if (!DigitalPublicUrlService.IsFirstExactDomain(domains, index) ||
                !DigitalPublicUrlService.TryGetNativePolicyHostname(mappings[index].Domain, out var domain))
                continue;
            yield return (mappings[index], domain);
        }
    }
}

/// <summary>Host/path URL builder shared by controllers, views, and fulfillment.</summary>
public sealed class DigitalPublicUrlService(
    DigitalProductsAppService apps,
    IOptions<BTCPayServerOptions> options)
{
    public const string LegacyController = "DigitalDownloadsPublic";
    public const string CleanController = "CleanDigitalDownloadsPublic";
    public const string CleanAssetsController = "CleanDigitalDownloadsAssets";
    public const string CleanPrefix = "/downloads";
    internal const string StoreItem = "MakePay.DigitalProducts.App.StoreId";
    internal const string DomainItem = "MakePay.DigitalProducts.App.Domain";
    private readonly string _rootPath = NormalizeRootPath(options.Value.RootPath);

    public static string NormalizeRootPath(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || rootPath == "/") return "";
        return "/" + rootPath.Trim('/');
    }

    public static string LegacyPrefix(string storeId) => $"/stores/{Uri.EscapeDataString(storeId)}/downloads";

    public static bool TryNormalizeHostname(string? value, out string hostname, out string? error)
    {
        hostname = "";
        error = null;
        var candidate = value?.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (candidate.Length > 253 || candidate.Contains('/') || candidate.Contains('\\') ||
            candidate.Contains(':') || candidate.Contains('@') || candidate.Contains('*') || candidate.Any(char.IsWhiteSpace))
        {
            error = "The mapped domain is not a valid hostname.";
            return false;
        }
        try
        {
            var idn = new IdnMapping { UseStd3AsciiRules = true };
            var labels = candidate.Split('.', StringSplitOptions.None);
            if (labels.Length < 2 || labels.Any(label => label.Length == 0)) return false;
            var ascii = labels.Select(label => idn.GetAscii(label).ToLowerInvariant()).ToArray();
            if (ascii.Any(label => label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-' ||
                                   label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
                return false;
            hostname = string.Join('.', ascii);
            return hostname.Length <= 253 && Uri.CheckHostName(hostname) == UriHostNameType.Dns;
        }
        catch (ArgumentException) { return false; }
    }

    public static string? NormalizeRequestHost(HostString host) =>
        TryNormalizeHostname(host.Host, out var normalized, out _) ? normalized : null;

    /// <summary>
    /// Native DomainMappingConstraint compares the stored value directly to
    /// Request.Host. A value that only becomes valid after trimming, removing
    /// a trailing dot, or IDN conversion is therefore not an active mapping.
    /// </summary>
    public static bool TryGetNativePolicyHostname(string? rawValue, out string hostname)
    {
        hostname = "";
        if (!TryNormalizeHostname(rawValue, out var normalized, out _)) return false;
        if (!string.Equals(rawValue, normalized, StringComparison.OrdinalIgnoreCase)) return false;
        hostname = normalized;
        return true;
    }

    /// <summary>Matches the first-row-wins behavior of BTCPay's native constraint.</summary>
    public static bool IsFirstExactDomain(IReadOnlyList<string?> domains, int candidateIndex)
    {
        if (candidateIndex < 0 || candidateIndex >= domains.Count || domains[candidateIndex] is not { } candidate)
            return false;
        for (var index = 0; index < candidateIndex; index++)
        {
            if (string.Equals(domains[index], candidate, StringComparison.InvariantCultureIgnoreCase))
                return false;
        }
        return true;
    }

    public static bool IsOnionBaseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        uri.DnsSafeHost.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);

    public static void SetMapping(HttpContext context, string storeId, string? domain)
    {
        context.Items[StoreItem] = storeId;
        if (domain is not null) context.Items[DomainItem] = domain;
    }

    public string ForRequest(HttpContext context, string storeId, string legacyUrl)
    {
        if (context.Request.Host.Host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase))
            return legacyUrl;
        if (!context.Items.TryGetValue(StoreItem, out var value) ||
            !string.Equals(value as string, storeId, StringComparison.OrdinalIgnoreCase) ||
            context.Items[DomainItem] is not string domain)
            return legacyUrl;
        var clean = RemoveStoreIdFromCleanUrl(
            RewriteLegacyUrl(storeId, legacyUrl, context.Request.PathBase), storeId, context.Request.PathBase);
        var requestHost = NormalizeRequestHost(context.Request.Host);
        return string.Equals(requestHost, domain, StringComparison.OrdinalIgnoreCase)
            ? clean
            : $"https://{domain}{clean}";
    }

    public async Task<string> CanonicalPath(string storeId, string legacyBaseUrl, string legacyPath)
    {
        var rootedLegacyPath = AddRootPath(legacyPath, _rootPath);
        if (IsOnionBaseUrl(legacyBaseUrl)) return rootedLegacyPath;
        var (_, domain) = await apps.MappingForStore(storeId);
        return domain is null ? rootedLegacyPath : RewriteLegacyUrl(storeId, legacyPath, new PathString(_rootPath));
    }

    public async Task<string> Absolute(string storeId, string legacyBaseUrl, string legacyPath)
    {
        if (IsOnionBaseUrl(legacyBaseUrl))
            return BuildLegacyAbsolute(legacyBaseUrl, legacyPath, _rootPath);
        var (_, domain) = await apps.MappingForStore(storeId);
        if (domain is not null)
            return $"https://{domain}{RewriteLegacyUrl(storeId, legacyPath, new PathString(_rootPath))}";
        return BuildLegacyAbsolute(legacyBaseUrl, legacyPath, _rootPath);
    }

    public static string BuildLegacyAbsolute(string legacyBaseUrl, string legacyPath, string? rootPath)
    {
        if (!Uri.TryCreate(legacyBaseUrl, UriKind.Absolute, out var root) ||
            (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("A valid HTTP(S) public base URL is required.");
        var rootedLegacyPath = AddRootPath(legacyPath, NormalizeRootPath(rootPath));
        return new Uri(new Uri(root.GetLeftPart(UriPartial.Authority)), rootedLegacyPath).AbsoluteUri;
    }

    public async Task<string> Origin(string storeId, string legacyBaseUrl)
    {
        if (IsOnionBaseUrl(legacyBaseUrl)) return BuildLegacyOrigin(legacyBaseUrl, _rootPath);
        var (_, domain) = await apps.MappingForStore(storeId);
        if (domain is not null) return $"https://{domain}{_rootPath}";
        return BuildLegacyOrigin(legacyBaseUrl, _rootPath);
    }

    public static string BuildLegacyOrigin(string legacyBaseUrl, string? rootPath)
    {
        if (!Uri.TryCreate(legacyBaseUrl, UriKind.Absolute, out var root) ||
            (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("A valid HTTP(S) public base URL is required.");
        return root.GetLeftPart(UriPartial.Authority).TrimEnd('/') + NormalizeRootPath(rootPath);
    }

    public static string AddRootPath(string value, string? rootPath)
    {
        var root = NormalizeRootPath(rootPath);
        if (root.Length == 0 || string.IsNullOrEmpty(value) || !value.StartsWith('/')) return value;
        if (value.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            return value;
        return root + value;
    }

    public static string RewriteLegacyUrl(string storeId, string value, PathString pathBase = default)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var root = pathBase.HasValue ? pathBase.Value!.TrimEnd('/') : "";
        var legacyPrefix = LegacyPrefix(storeId);
        var prefix = root + legacyPrefix;
        if (root.Length > 0 && value.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
            prefix = legacyPrefix;
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return value;
        if (value.Length > prefix.Length && value[prefix.Length] is not ('/' or '?' or '#')) return value;
        return root + CleanPrefix + value[prefix.Length..];
    }

    public static string RemoveStoreIdFromCleanUrl(string value, string storeId, PathString pathBase = default)
    {
        var queryIndex = value.IndexOf('?');
        if (queryIndex < 0) return value;
        var root = pathBase.HasValue ? pathBase.Value!.TrimEnd('/') : "";
        var path = value[..queryIndex];
        var cleanPrefix = root + CleanPrefix;
        if (!path.StartsWith(cleanPrefix, StringComparison.OrdinalIgnoreCase) ||
            (path.Length > cleanPrefix.Length && path[cleanPrefix.Length] != '/'))
            return value;

        var fragmentIndex = value.IndexOf('#', queryIndex);
        var query = value[(queryIndex + 1)..(fragmentIndex < 0 ? value.Length : fragmentIndex)];
        var fragment = fragmentIndex < 0 ? "" : value[fragmentIndex..];
        var kept = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part =>
            {
                var separator = part.IndexOf('=');
                var name = separator < 0 ? part : part[..separator];
                var rawValue = separator < 0 ? "" : part[(separator + 1)..];
                return !name.Equals("storeId", StringComparison.OrdinalIgnoreCase) ||
                       !Uri.UnescapeDataString(rawValue.Replace('+', ' ')).Equals(storeId, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
        return path + (kept.Length == 0 ? "" : "?" + string.Join('&', kept)) + fragment;
    }

    public static string? CleanUrlFromLegacy(
        string? mappedBaseUrl,
        string storeId,
        PathString pathBase,
        PathString path,
        QueryString query)
    {
        if (string.IsNullOrWhiteSpace(mappedBaseUrl)) return null;
        var legacyPrefix = pathBase.Add(new PathString(LegacyPrefix(storeId)));
        var currentPath = pathBase.Add(path);
        if (!currentPath.StartsWithSegments(legacyPrefix, out var remainder)) return null;
        return mappedBaseUrl.TrimEnd('/') + new PathString(CleanPrefix).Add(remainder) + query;
    }

    public async Task<string?> MappedBaseUrl(string storeId)
    {
        var (_, domain) = await apps.MappingForStore(storeId);
        return domain is null ? null : $"https://{domain}{_rootPath}";
    }
}
