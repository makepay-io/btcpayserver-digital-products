#nullable enable
using System.Text.Json;
using BTCPayServer.Plugins.MakePay.LicenseManager.Models;
using BTCPayServer.Plugins.MakePay.LicenseManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Plugins.MakePay.LicenseManager.Controllers;

[ApiController]
[Route("api/v1/stores/{storeId}/licenses")]
public sealed class LicenseApiController(LicenseRepository repository, LicenseSecurityService security, IRateLimitService rateLimits) : ControllerBase
{
    [HttpPost("{actionName:regex(^verify|activate|deactivate|heartbeat$)}")]
    public async Task<IActionResult> Execute(string storeId, string actionName, [FromBody] LicenseApiRequest request, CancellationToken cancellationToken)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!await rateLimits.Throttle(ZoneLimits.PublicInvoices, $"license-api:{storeId}:{ip}", cancellationToken)) return StatusCode(StatusCodes.Status429TooManyRequests);
        var settings = await repository.GetSettings(storeId);
        if (!LicenseSecurityService.ValidHeaderConfiguration(settings.LicenseKeyHeader, settings.SignatureHeader, settings.TimestampHeader, settings.NonceHeader, settings.ResponseSignatureHeader)) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "invalid_header_configuration" });
        if (!security.AllowRequest($"{storeId}:{ip}", settings.RequestsPerMinute, DateTimeOffset.UtcNow)) return StatusCode(StatusCodes.Status429TooManyRequests);
        var key = Request.Headers[settings.LicenseKeyHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key)) return UnauthorizedResponse(settings, "missing_license", "License key header is required.");
        var timestamp = Request.Headers[settings.TimestampHeader].FirstOrDefault() ?? "";
        var nonce = Request.Headers[settings.NonceHeader].FirstOrDefault() ?? "";
        if (settings.RequireSignedRequests)
        {
            var secret = security.Unprotect(settings.ProtectedApiSecret);
            var supplied = Request.Headers[settings.SignatureHeader].FirstOrDefault() ?? "";
            if (secret is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "api_not_configured" });
            if (!long.TryParse(timestamp, out var unix) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unix) > settings.MaxClockSkewSeconds) return UnauthorizedResponse(settings, "stale_request", "Timestamp is outside the allowed window.");
            if (nonce.Length is < 16 or > 128 || !security.AcceptNonce(storeId, nonce, DateTimeOffset.UtcNow, settings.MaxClockSkewSeconds)) return UnauthorizedResponse(settings, "replayed_request", "Nonce is missing or already used.");
            if (!security.Verify(secret, LicenseSecurityService.CanonicalRequest(timestamp, nonce, actionName, key, request.InstanceId), supplied)) return UnauthorizedResponse(settings, "bad_signature", "Request signature is invalid.");
        }
        var keyHash = LicenseSecurityService.HashLicenseKey(key);
        var license = await repository.FindByKeyHash(storeId, keyHash);
        if (license is null) return Signed(settings, timestamp, new LicenseApiResponse(false, "not_found", null, null, 0, 0, "License not found."), StatusCodes.Status404NotFound);
        var now = DateTimeOffset.UtcNow;
        if (license.ExpiresAt <= now && license.Status == ManagedLicenseStatus.Active) { license.Status = ManagedLicenseStatus.Expired; await repository.SaveLicense(storeId, license); }
        var instanceHash = LicenseSecurityService.HashInstance(request.InstanceId);
        var success = false; string? message = null;
        if (license.Status == ManagedLicenseStatus.Active)
        {
            var activation = license.Activations.FirstOrDefault(a => a.InstanceHash == instanceHash);
            switch (actionName.ToLowerInvariant())
            {
                case "verify": success = true; break;
                case "activate" when activation is not null: activation.LastSeenAt = now; activation.Label = request.InstanceLabel; success = true; break;
                case "activate" when license.Activations.Count < license.MaxActivations: license.Activations.Add(new() { InstanceHash = instanceHash, Label = request.InstanceLabel, ActivatedAt = now, LastSeenAt = now }); success = true; break;
                case "activate": message = "Activation limit reached."; break;
                case "deactivate" when activation is not null: license.Activations.Remove(activation); success = true; break;
                case "deactivate": success = true; break;
                case "heartbeat" when activation is not null: activation.LastSeenAt = now; success = true; break;
                case "heartbeat": message = "Instance is not activated."; break;
            }
        }
        else message = "License is " + license.Status.ToString().ToLowerInvariant() + ".";
        license.Audit.Add(new() { Action = actionName.ToLowerInvariant(), Success = success, InstanceHashPrefix = instanceHash[..12], IpHashPrefix = LicenseSecurityService.HashInstance(ip)[..12], Detail = message });
        if (license.Audit.Count > 200) license.Audit.RemoveRange(0, license.Audit.Count - 200);
        await repository.SaveLicense(storeId, license);
        return Signed(settings, timestamp, new LicenseApiResponse(success, license.Status.ToString().ToLowerInvariant(), license.ProductId, license.ExpiresAt, license.Activations.Count, license.MaxActivations, message), success ? StatusCodes.Status200OK : StatusCodes.Status409Conflict);
    }

    private IActionResult UnauthorizedResponse(LicenseManagerSettings settings, string status, string message) => Signed(settings, "", new LicenseApiResponse(false, status, null, null, 0, 0, message), StatusCodes.Status401Unauthorized);
    private IActionResult Signed(LicenseManagerSettings settings, string timestamp, LicenseApiResponse response, int status)
    {
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (security.Unprotect(settings.ProtectedApiSecret) is { } secret) Response.Headers[settings.ResponseSignatureHeader] = security.Sign(secret, LicenseSecurityService.CanonicalResponse(timestamp, json));
        return new ContentResult { Content = json, ContentType = "application/json", StatusCode = status };
    }
}
