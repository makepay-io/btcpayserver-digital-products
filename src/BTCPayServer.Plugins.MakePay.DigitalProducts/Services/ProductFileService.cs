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

    public async Task<(string RelativePath, string FileName, string ContentType, long Size)> SaveLocal(
        string storeId, string productId, IFormFile upload, CancellationToken cancellationToken)
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
        return (relative, cleanName, string.IsNullOrWhiteSpace(upload.ContentType) ? "application/octet-stream" : upload.ContentType, upload.Length);
    }

    public async Task<RemoteFile> Open(DigitalProduct product, DigitalDownloadsSettings settings, CancellationToken cancellationToken)
    {
        return product.StorageKind switch
        {
            ProductStorageKind.Local => OpenLocal(product),
            ProductStorageKind.CustomUrl => await OpenRemote(product, settings, cancellationToken),
            ProductStorageKind.S3 => await OpenS3(product, settings, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported product storage provider.")
        };
    }

    private RemoteFile OpenLocal(DigitalProduct product)
    {
        if (string.IsNullOrWhiteSpace(product.StorageLocation)) throw new FileNotFoundException("Product file is not configured.");
        var fullPath = Path.GetFullPath(Path.Combine(_root, product.StorageLocation));
        var root = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.Ordinal)) throw new UnauthorizedAccessException("Invalid product path.");
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return new RemoteFile(stream, product.ContentType, stream.Length, null);
    }

    private async Task<RemoteFile> OpenRemote(DigitalProduct product, DigitalDownloadsSettings settings, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(product.StorageLocation, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("A valid HTTPS or HTTP source URL is required.");
        await ValidatePublicOrigin(uri, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var authValue = secrets.UnprotectSecret(settings.ProtectedRemoteAuthorizationValue);
        if (!string.IsNullOrWhiteSpace(settings.RemoteAuthorizationHeader) && !IsSafeHeaderName(settings.RemoteAuthorizationHeader))
            throw new InvalidOperationException("The remote authorization header name is invalid.");
        if (!string.IsNullOrWhiteSpace(authValue) && (authValue.Contains('\r') || authValue.Contains('\n')))
            throw new InvalidOperationException("The remote authorization value is invalid.");
        if (!string.IsNullOrWhiteSpace(settings.RemoteAuthorizationHeader) && !string.IsNullOrWhiteSpace(authValue))
            request.Headers.TryAddWithoutValidation(settings.RemoteAuthorizationHeader, authValue);
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return new RemoteFile(await response.Content.ReadAsStreamAsync(cancellationToken),
            response.Content.Headers.ContentType?.ToString() ?? product.ContentType,
            response.Content.Headers.ContentLength, response);
    }

    private async Task<RemoteFile> OpenS3(DigitalProduct product, DigitalDownloadsSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.S3Bucket) || string.IsNullOrWhiteSpace(settings.S3AccessKey))
            throw new InvalidOperationException("S3 bucket and credentials are required.");
        var secret = secrets.UnprotectSecret(settings.ProtectedS3SecretKey);
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("S3 secret key is unavailable.");
        var endpoint = new Uri(settings.S3Endpoint.TrimEnd('/') + "/");
        await ValidatePublicOrigin(endpoint, cancellationToken);
        var key = (product.StorageLocation ?? "").TrimStart('/');
        var path = $"/{Uri.EscapeDataString(settings.S3Bucket)}/{string.Join('/', key.Split('/').Select(Uri.EscapeDataString))}";
        var uri = new Uri(endpoint, path.TrimStart('/'));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        SignAwsV4(request, settings.S3AccessKey, secret, settings.S3Region, "s3");
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return new RemoteFile(await response.Content.ReadAsStreamAsync(cancellationToken),
            response.Content.Headers.ContentType?.ToString() ?? product.ContentType,
            response.Content.Headers.ContentLength, response);
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
    private static string SafeSegment(string value) => string.Concat(value.Where(char.IsLetterOrDigit));
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

public sealed class RemoteFile(Stream stream, string contentType, long? length, HttpResponseMessage? response) : IAsyncDisposable
{
    public Stream Stream { get; } = stream;
    public string ContentType { get; } = contentType;
    public long? Length { get; } = length;
    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        response?.Dispose();
    }
}
