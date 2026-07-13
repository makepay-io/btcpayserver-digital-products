#nullable enable
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

public sealed class DownloadTokenService(IDataProtectionProvider protectionProvider)
{
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("MakePay.DigitalDownloads.v1");

    public (string Token, string Hash, string ProtectedToken) Create()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, Hash(token), _protector.Protect(token));
    }

    public bool Verify(string token, string expectedHash)
    {
        var actual = Convert.FromHexString(Hash(token));
        var expected = Convert.FromHexString(expectedHash);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public string? Unprotect(string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken)) return null;
        try { return _protector.Unprotect(protectedToken); }
        catch (CryptographicException) { return null; }
    }

    public string ProtectSecret(string secret) => _protector.Protect(secret);
    public string? UnprotectSecret(string? protectedSecret) => Unprotect(protectedSecret);
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
