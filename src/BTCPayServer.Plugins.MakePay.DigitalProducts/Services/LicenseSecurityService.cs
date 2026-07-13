#nullable enable
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace BTCPayServer.Plugins.MakePay.LicenseManager.Services;

public sealed class LicenseSecurityService(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("MakePay.LicenseManager.v1");
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonces = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, WindowCounter> _requestWindows = new(StringComparer.Ordinal);

    public string Protect(string value) => _protector.Protect(value);
    public string? Unprotect(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; try { return _protector.Unprotect(value); } catch (CryptographicException) { return null; } }
    public static string NormalizeKey(string value) => value.Trim().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
    public static string HashLicenseKey(string value) => HexSha256(NormalizeKey(value));
    public static string HashInstance(string value) => HexSha256(value.Trim());
    public static string GenerateApiSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static bool IsSafeApiHeaderName(string value) => value.StartsWith("X-", StringComparison.OrdinalIgnoreCase) && value.Length <= 80 && value.All(ch => char.IsAsciiLetterOrDigit(ch) || "!#$%&'*+-.^_`|~".Contains(ch));
    public static bool ValidHeaderConfiguration(params string[] values) => values.All(IsSafeApiHeaderName) && values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Length;

    public string Sign(string secret, string canonical) => Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(canonical)));
    public bool Verify(string secret, string canonical, string supplied)
    {
        byte[] expected;
        byte[] actual;
        try { expected = Base64UrlDecode(Sign(secret, canonical)); actual = Base64UrlDecode(supplied); }
        catch (FormatException) { return false; }
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public bool AcceptNonce(string storeId, string nonce, DateTimeOffset now, int skewSeconds)
    {
        foreach (var stale in _nonces.Where(pair => pair.Value < now.AddSeconds(-skewSeconds * 2)).Select(pair => pair.Key).Take(100)) _nonces.TryRemove(stale, out _);
        return _nonces.TryAdd(storeId + ":" + nonce, now);
    }

    public bool AllowRequest(string key, int perMinute, DateTimeOffset now)
    {
        var minute = now.ToUnixTimeSeconds() / 60;
        var counter = _requestWindows.GetOrAdd(key, _ => new WindowCounter());
        lock (counter)
        {
            if (counter.Minute != minute) { counter.Minute = minute; counter.Count = 0; }
            return ++counter.Count <= perMinute;
        }
    }

    public static string CanonicalRequest(string timestamp, string nonce, string action, string licenseKey, string instanceId) =>
        string.Join('\n', timestamp, nonce, action.ToLowerInvariant(), NormalizeKey(licenseKey), instanceId.Trim());
    public static string CanonicalResponse(string timestamp, string body) => timestamp + "\n" + body;
    private static string HexSha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string value) { var padded = value.Replace('-', '+').Replace('_', '/'); padded += new string('=', (4 - padded.Length % 4) % 4); return Convert.FromBase64String(padded); }
    private sealed class WindowCounter { public long Minute; public int Count; }
}
