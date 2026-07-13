#nullable enable
using System.Globalization;
using System.Net.Http.Headers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using BTCPayServer.Configuration;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

public sealed class ProductFileService(HttpClient httpClient, IOptions<DataDirectories> directories, DownloadTokenService secrets)
{
    private readonly string _root = Path.Combine(directories.Value.DataDir, "MakePayDigitalDownloads");
    private const long MaxStorefrontAssetBytes = 10 * 1024 * 1024;
    // Stay below the default 100 MB reverse-proxy request limit on BTCPay deployments.
    private const long MaxPreviewAssetBytes = 95L * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> StorefrontImageExtensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif"
    };

    public async Task<(string RelativePath, string FileName, string ContentType, long Size)> SaveLocal(
        string storeId, string productId, IFormFile upload, CancellationToken cancellationToken, DigitalProductType? productType = null)
    {
        if (upload.Length <= 0) throw new InvalidOperationException("The uploaded file is empty.");
        var cleanName = Path.GetFileName(upload.FileName);
        if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "download.bin";
        var directory = Path.Combine(_root, SafeSegment(storeId), SafeSegment(productId));
        Directory.CreateDirectory(directory);
        var internalName = Guid.NewGuid().ToString("N") + Path.GetExtension(cleanName);
        var fullPath = Path.Combine(directory, internalName);
        await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await upload.CopyToAsync(output, cancellationToken);
        var relative = Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
        return (relative, cleanName, ResolveUploadContentType(upload, productType), upload.Length);
    }

    public async Task<DigitalProductPreviewAsset> SavePreviewLocal(
        string storeId,
        string productId,
        string label,
        IFormFile upload,
        CancellationToken cancellationToken,
        DigitalProductType? productType = null)
    {
        if (upload.Length <= 0) throw new InvalidOperationException("The preview file is empty.");
        if (upload.Length > MaxPreviewAssetBytes) throw new InvalidOperationException("Preview and demo files must be 95 MB or smaller.");
        var cleanName = Path.GetFileName(upload.FileName);
        if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "preview.bin";
        var assetId = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_root, SafeSegment(storeId), SafeSegment(productId), "previews", assetId);
        Directory.CreateDirectory(directory);
        var internalName = Guid.NewGuid().ToString("N") + Path.GetExtension(cleanName);
        var fullPath = Path.Combine(directory, internalName);
        await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await upload.CopyToAsync(output, cancellationToken);
        return new DigitalProductPreviewAsset
        {
            Id = assetId,
            Label = string.IsNullOrWhiteSpace(label) ? "Preview" : label.Trim(),
            StorageKind = ProductStorageKind.Local,
            StorageLocation = Path.GetRelativePath(_root, fullPath).Replace('\\', '/'),
            FileName = cleanName,
            ContentType = ResolveUploadContentType(upload, productType),
            FileSize = upload.Length
        };
    }

    public async Task<string?> ValidatePreviewAsset(
        DigitalProductType productType,
        IFormFile upload,
        CancellationToken cancellationToken)
    {
        if (upload.Length <= 0) return "The preview file is empty.";
        if (upload.Length > MaxPreviewAssetBytes) return "Preview and demo files must be 95 MB or smaller.";
        var contentType = ResolveUploadContentType(upload, productType);
        var allowed = productType switch
        {
            DigitalProductType.PdfEbook => contentType == "application/pdf",
            DigitalProductType.Audio => contentType is "audio/mpeg" or "audio/wav" or "audio/x-wav" or "audio/ogg" or "audio/mp4" or "audio/webm" or "audio/flac",
            DigitalProductType.Video => contentType is "video/mp4" or "video/webm" or "video/ogg",
            DigitalProductType.PhotosArt => contentType is "image/png" or "image/jpeg" or "image/webp",
            _ => contentType is "application/pdf" or "image/png" or "image/jpeg" or "image/webp"
        };
        if (!allowed) return $"The selected preview type is not valid for {DigitalStorefrontBuilder.ProductTypeLabel(productType)}.";
        await using var stream = upload.OpenReadStream();
        var header = new byte[16];
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        return SignatureMatches(contentType, header.AsSpan(0, read)) ? null : "The preview file contents do not match its declared format.";
    }

    public async Task<string?> ValidateProductAsset(
        DigitalProductType productType,
        IFormFile upload,
        CancellationToken cancellationToken)
    {
        if (upload.Length <= 0) return "The protected product file is empty.";
        if (productType == DigitalProductType.FileDownload) return null;

        var contentType = ResolveUploadContentType(upload, productType);
        var allowed = productType switch
        {
            DigitalProductType.PdfEbook => contentType == "application/pdf",
            DigitalProductType.Audio => contentType is "audio/mpeg" or "audio/wav" or "audio/x-wav" or "audio/ogg" or "audio/mp4" or "audio/webm" or "audio/flac",
            DigitalProductType.Video => contentType is "video/mp4" or "video/webm" or "video/ogg",
            DigitalProductType.PhotosArt => contentType is "image/png" or "image/jpeg" or "image/webp" or "application/zip",
            _ => false
        };
        if (!allowed) return $"The protected file type is not valid for {DigitalStorefrontBuilder.ProductTypeLabel(productType)}.";

        await using var stream = upload.OpenReadStream();
        var header = new byte[16];
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        return SignatureMatches(contentType, header.AsSpan(0, read))
            ? null
            : "The protected file contents do not match its declared format.";
    }

    public async Task<(string FileName, string ContentType, long Size)> SaveStorefrontAsset(
        string storeId,
        string assetId,
        IFormFile upload,
        CancellationToken cancellationToken)
    {
        if (ValidateStorefrontAsset(upload) is { } validationError) throw new InvalidOperationException(validationError);
        var contentType = upload.ContentType?.Split(';', 2)[0].Trim() ?? "";
        var extension = StorefrontImageExtensions[contentType];

        var directory = Path.Combine(_root, "StorefrontAssets", SafeSegment(storeId), SafeAssetSegment(assetId));
        Directory.CreateDirectory(directory);
        var fileName = Guid.NewGuid().ToString("N") + extension;
        var fullPath = Path.Combine(directory, fileName);
        await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await upload.CopyToAsync(output, cancellationToken);

        foreach (var stale in Directory.EnumerateFiles(directory).Where(path => !path.Equals(fullPath, StringComparison.Ordinal)))
            File.Delete(stale);
        return (fileName, contentType, upload.Length);
    }

    public static string? ValidateStorefrontAsset(IFormFile upload)
    {
        if (upload.Length <= 0) return "The uploaded image is empty.";
        if (upload.Length > MaxStorefrontAssetBytes) return "Storefront images must be 10 MB or smaller.";
        var contentType = upload.ContentType?.Split(';', 2)[0].Trim() ?? "";
        return StorefrontImageExtensions.ContainsKey(contentType) ? null : "Use a PNG, JPEG, WebP, or GIF image.";
    }

    public RemoteFile? OpenStorefrontAsset(string storeId, string assetId, string fileName)
    {
        if (SafeAssetSegment(assetId) != assetId || Path.GetFileName(fileName) != fileName) return null;
        var directory = Path.Combine(_root, "StorefrontAssets", SafeSegment(storeId), SafeAssetSegment(assetId));
        var fullPath = Path.GetFullPath(Path.Combine(directory, fileName));
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.Ordinal) || !File.Exists(fullPath)) return null;
        var extension = Path.GetExtension(fileName);
        var contentType = StorefrontImageExtensions.FirstOrDefault(item => item.Value.Equals(extension, StringComparison.OrdinalIgnoreCase)).Key;
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return new RemoteFile(stream, contentType, stream.Length, null);
    }

    public async Task<RemoteFile> Open(
        string storeId,
        DigitalProduct product,
        DigitalDownloadsSettings settings,
        RangeHeaderValue? range,
        CancellationToken cancellationToken)
    {
        return product.StorageKind switch
        {
            ProductStorageKind.Local => OpenLocal(storeId, product.Id, product.StorageLocation, product.ContentType),
            ProductStorageKind.CustomUrl => await OpenRemote(product.StorageLocation, product.ContentType, settings, range, cancellationToken),
            ProductStorageKind.S3 => await OpenS3(product.StorageLocation, product.ContentType, settings, range, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported product storage provider.")
        };
    }

    public RemoteFile OpenPreview(string storeId, string productId, DigitalProductPreviewAsset asset)
    {
        if (asset.StorageKind != ProductStorageKind.Local) throw new InvalidOperationException("Public preview assets must be stored locally.");
        var boundary = Path.Combine(_root, SafeSegment(storeId), SafeSegment(productId), "previews", SafeAssetSegment(asset.Id));
        return OpenLocalWithin(boundary, asset.StorageLocation, asset.ContentType);
    }

    private RemoteFile OpenLocal(string storeId, string productId, string? storageLocation, string contentType)
    {
        var boundary = Path.GetFullPath(Path.Combine(_root, SafeSegment(storeId), SafeSegment(productId))) + Path.DirectorySeparatorChar;
        return OpenLocalWithin(boundary, storageLocation, contentType);
    }

    private RemoteFile OpenLocalWithin(string boundaryPath, string? storageLocation, string contentType)
    {
        if (string.IsNullOrWhiteSpace(storageLocation)) throw new FileNotFoundException("Product file is not configured.");
        var boundary = Path.GetFullPath(boundaryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(_root, storageLocation));
        if (!fullPath.StartsWith(boundary, StringComparison.Ordinal)) throw new UnauthorizedAccessException("Invalid product path.");
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return new RemoteFile(stream, contentType, stream.Length, null, false, null, true);
    }

    private async Task<RemoteFile> OpenRemote(string? storageLocation, string contentType, DigitalDownloadsSettings settings, RangeHeaderValue? range, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(storageLocation, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("A valid HTTPS or HTTP source URL is required.");
        await ValidatePublicOrigin(uri, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        var authValue = secrets.UnprotectSecret(settings.ProtectedRemoteAuthorizationValue);
        if (!string.IsNullOrWhiteSpace(settings.RemoteAuthorizationHeader) && !IsSafeHeaderName(settings.RemoteAuthorizationHeader))
            throw new InvalidOperationException("The remote authorization header name is invalid.");
        if (!string.IsNullOrWhiteSpace(authValue) && (authValue.Contains('\r') || authValue.Contains('\n')))
            throw new InvalidOperationException("The remote authorization value is invalid.");
        if (!string.IsNullOrWhiteSpace(settings.RemoteAuthorizationHeader) && !string.IsNullOrWhiteSpace(authValue))
            request.Headers.TryAddWithoutValidation(settings.RemoteAuthorizationHeader, authValue);
        request.Headers.Range = range;
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            var contentRange = response.Content.Headers.ContentRange?.ToString();
            response.Dispose();
            throw new RemoteRangeNotSatisfiableException(contentRange);
        }
        response.EnsureSuccessStatusCode();
        return new RemoteFile(await response.Content.ReadAsStreamAsync(cancellationToken),
            // The merchant-configured type is validated for inline media. Do not trust a
            // remote origin to turn a protected stream into same-origin active HTML.
            contentType,
            response.Content.Headers.ContentLength, response,
            response.StatusCode == HttpStatusCode.PartialContent,
            response.Content.Headers.ContentRange?.ToString(),
            response.Headers.AcceptRanges.Contains("bytes"));
    }

    private async Task<RemoteFile> OpenS3(string? storageLocation, string contentType, DigitalDownloadsSettings settings, RangeHeaderValue? range, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.S3Bucket) || string.IsNullOrWhiteSpace(settings.S3AccessKey))
            throw new InvalidOperationException("S3 bucket and credentials are required.");
        var secret = secrets.UnprotectSecret(settings.ProtectedS3SecretKey);
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("S3 secret key is unavailable.");
        var endpoint = new Uri(settings.S3Endpoint.TrimEnd('/') + "/");
        await ValidatePublicOrigin(endpoint, cancellationToken);
        var key = (storageLocation ?? "").TrimStart('/');
        var path = $"/{Uri.EscapeDataString(settings.S3Bucket)}/{string.Join('/', key.Split('/').Select(Uri.EscapeDataString))}";
        var uri = new Uri(endpoint, path.TrimStart('/'));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        request.Headers.Range = range;
        SignAwsV4(request, settings.S3AccessKey, secret, settings.S3Region, "s3");
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            var contentRange = response.Content.Headers.ContentRange?.ToString();
            response.Dispose();
            throw new RemoteRangeNotSatisfiableException(contentRange);
        }
        response.EnsureSuccessStatusCode();
        return new RemoteFile(await response.Content.ReadAsStreamAsync(cancellationToken),
            contentType,
            response.Content.Headers.ContentLength, response,
            response.StatusCode == HttpStatusCode.PartialContent,
            response.Content.Headers.ContentRange?.ToString(),
            response.Headers.AcceptRanges.Contains("bytes"));
    }

    private static void SignAwsV4(HttpRequestMessage request, string accessKey, string secretKey, string region, string service)
    {
        var now = DateTimeOffset.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        const string payloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var host = request.RequestUri!.IsDefaultPort ? request.RequestUri.Host : request.RequestUri.Authority;
        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        var canonicalHeaders = $"host:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n";
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var canonical = $"GET\n{request.RequestUri.AbsolutePath}\n{request.RequestUri.Query.TrimStart('?')}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var scope = $"{date}/{region}/{service}/aws4_request";
        var toSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{HexSha256(canonical)}";
        var dateKey = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretKey), date);
        var regionKey = Hmac(dateKey, region);
        var serviceKey = Hmac(regionKey, service);
        var signingKey = Hmac(serviceKey, "aws4_request");
        var signature = Convert.ToHexString(Hmac(signingKey, toSign)).ToLowerInvariant();
        request.Headers.Authorization = new AuthenticationHeaderValue("AWS4-HMAC-SHA256", $"Credential={accessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private static byte[] Hmac(byte[] key, string value) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
    private static string HexSha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string NormalizeContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "application/octet-stream";
        var normalized = value.Split(';', 2)[0].Trim().ToLowerInvariant();
        return normalized == "application/x-zip-compressed" ? "application/zip" : normalized;
    }
    private static string ResolveUploadContentType(IFormFile upload, DigitalProductType? productType)
    {
        var declared = NormalizeContentType(upload.ContentType);
        if (declared != "application/octet-stream") return declared;
        return Path.GetExtension(upload.FileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".flac" => "audio/flac",
            ".ogg" => productType == DigitalProductType.Video ? "video/ogg" : "audio/ogg",
            ".mp4" => productType == DigitalProductType.Audio ? "audio/mp4" : "video/mp4",
            ".webm" => productType == DigitalProductType.Audio ? "audio/webm" : "video/webm",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".zip" => "application/zip",
            _ => declared
        };
    }
    private static bool SignatureMatches(string contentType, ReadOnlySpan<byte> header) => contentType switch
    {
        "application/pdf" => header.StartsWith("%PDF-"u8),
        "image/png" => header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        "image/webp" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
        "audio/mpeg" => header.StartsWith("ID3"u8) || header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0,
        "audio/wav" or "audio/x-wav" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WAVE"u8),
        "audio/ogg" or "video/ogg" => header.StartsWith("OggS"u8),
        "audio/mp4" or "video/mp4" => header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8),
        "audio/webm" or "video/webm" => header.Length >= 4 && header[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
        "audio/flac" => header.StartsWith("fLaC"u8),
        "application/zip" => header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B &&
                             ((header[2] == 0x03 && header[3] == 0x04) || (header[2] == 0x05 && header[3] == 0x06) || (header[2] == 0x07 && header[3] == 0x08)),
        _ => false
    };
    private static string SafeSegment(string value) => string.Concat(value.Where(char.IsLetterOrDigit));
    private static string SafeAssetSegment(string value) => string.Concat(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_'));
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None)) return false;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal && !address.IsIPv6Multicast && (address.GetAddressBytes()[0] & 0xFE) != 0xFC;
        var b = address.GetAddressBytes();
        return !(b[0] is 0 or 10 or 127 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 100 && b[1] is >= 64 and <= 127 || b[0] == 198 && b[1] is 18 or 19 || b[0] >= 224);
    }
    public static bool IsSafeHeaderName(string value) => value.Length is > 0 and <= 80 && value.All(ch => char.IsAsciiLetterOrDigit(ch) || "!#$%&'*+-.^_`|~".Contains(ch)) && !new[] { "Host", "Content-Length", "Connection", "Transfer-Encoding", "Cookie", "Set-Cookie" }.Contains(value, StringComparer.OrdinalIgnoreCase);
    private static async Task ValidatePublicOrigin(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme is not ("http" or "https") || uri.IsLoopback) throw new InvalidOperationException("Private or non-HTTP origins are not allowed.");
        var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address))) throw new InvalidOperationException("The configured origin resolves to a private or reserved network address.");
    }
}

public sealed class RemoteRangeNotSatisfiableException(string? contentRange = null) : Exception
{
    public string? ContentRange { get; } = contentRange;
}

public sealed class RemoteFile(
    Stream stream,
    string contentType,
    long? length,
    HttpResponseMessage? response,
    bool isPartial = false,
    string? contentRange = null,
    bool acceptsRanges = false) : IAsyncDisposable
{
    public Stream Stream { get; } = stream;
    public string ContentType { get; } = contentType;
    public long? Length { get; } = length;
    public bool IsPartial { get; } = isPartial;
    public string? ContentRange { get; } = contentRange;
    public bool AcceptsRanges { get; } = acceptsRanges;
    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        response?.Dispose();
    }
}
