#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using Microsoft.AspNetCore.DataProtection;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Services;

public sealed class CustomerAccessService(IDataProtectionProvider protection)
{
    private readonly IDataProtector _session = protection.CreateProtector("MakePay.DigitalProducts.CustomerSession.v1");
    private readonly IDataProtector _checkout = protection.CreateProtector("MakePay.DigitalProducts.CheckoutAccess.v1");
    private readonly IDataProtector _loginCode = protection.CreateProtector("MakePay.DigitalProducts.LoginCode.v1");

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public (CustomerLoginChallenge Challenge, string Code) CreateChallenge(string email, int minutes)
    {
        var challenge = new CustomerLoginChallenge
        {
            NormalizedEmail = NormalizeEmail(email),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(minutes)
        };
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        challenge.ProtectedCode = _loginCode.Protect(LoginCodePayload(challenge.Id, challenge.NormalizedEmail, code));
        return (challenge, code);
    }

    public bool Verify(CustomerLoginChallenge challenge, string code)
    {
        if (challenge.ConsumedAt is not null || challenge.ExpiresAt <= DateTimeOffset.UtcNow || challenge.Attempts >= 6 || string.IsNullOrWhiteSpace(challenge.ProtectedCode)) return false;
        try
        {
            var actual = _loginCode.Unprotect(challenge.ProtectedCode);
            return FixedTimeEquals(actual, LoginCodePayload(challenge.Id, challenge.NormalizedEmail, code.Trim()));
        }
        catch { return false; }
    }

    public string CreateSession(string storeId, string email, int hours) =>
        _session.Protect(JsonSerializer.Serialize(new CustomerSession(storeId, NormalizeEmail(email), DateTimeOffset.UtcNow.AddHours(hours))));

    public string? ReadSession(string? protectedSession, string storeId)
    {
        if (string.IsNullOrWhiteSpace(protectedSession)) return null;
        try
        {
            var session = JsonSerializer.Deserialize<CustomerSession>(_session.Unprotect(protectedSession));
            return session is not null && session.StoreId == storeId && session.ExpiresAt > DateTimeOffset.UtcNow
                ? session.Email
                : null;
        }
        catch { return null; }
    }

    public string CreateCheckoutAccess(DigitalCheckout checkout)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        checkout.PublicAccessTokenHash = DownloadTokenService.Hash(token);
        checkout.ProtectedPublicAccessToken = _checkout.Protect(token);
        return token;
    }

    public string? RecoverCheckoutAccess(DigitalCheckout checkout)
    {
        if (string.IsNullOrWhiteSpace(checkout.ProtectedPublicAccessToken)) return null;
        try { return _checkout.Unprotect(checkout.ProtectedPublicAccessToken); }
        catch { return null; }
    }

    public bool CanAccess(DigitalCheckout checkout, string? token, string? sessionEmail = null)
    {
        if (!string.IsNullOrWhiteSpace(sessionEmail) && NormalizeEmail(sessionEmail) == NormalizeEmail(checkout.BuyerEmail)) return true;
        return !string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(checkout.PublicAccessTokenHash) &&
               FixedTimeEquals(checkout.PublicAccessTokenHash, DownloadTokenService.Hash(token));
    }

    private static string LoginCodePayload(string challengeId, string email, string code) => $"{challengeId}\n{email}\n{code}";

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private sealed record CustomerSession(string StoreId, string Email, DateTimeOffset ExpiresAt);
}
