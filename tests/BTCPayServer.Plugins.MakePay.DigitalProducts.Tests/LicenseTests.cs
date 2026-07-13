using BTCPayServer.Plugins.MakePay.LicenseManager.Services;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.LicenseManager.Tests;
public class LicenseTests
{
    [Fact] public void GeneratorFollowsPattern() { var key = new LicenseKeyGenerator().Generate("MP-{A:4}-{X:6}-{N:4}-{HEX:8}-{YEAR}"); Assert.Matches("^MP-[A-Z]{4}-[A-Z2-9]{6}-[2-9]{4}-[0-9A-F]{8}-[0-9]{4}$", key); }
    [Fact] public void GeneratorRejectsUnknownOrOversizedTokens() { var generator = new LicenseKeyGenerator(); Assert.Throws<FormatException>(() => generator.Generate("{UNKNOWN}")); Assert.Throws<FormatException>(() => generator.Generate("{A:65}")); }
    [Fact] public void LicenseHashNormalizesCaseAndSpaces() { Assert.Equal(LicenseSecurityService.HashLicenseKey(" ab-cd "), LicenseSecurityService.HashLicenseKey("AB-CD")); }
    [Fact] public void CanonicalRequestIsDeterministic() { Assert.Equal("123\nnonce\nactivate\nAB-CD\ndevice", LicenseSecurityService.CanonicalRequest("123", "nonce", "ACTIVATE", "ab-cd", "device")); }
    [Fact] public void ApiHeadersMustBeUniqueCustomHeaders() { Assert.True(LicenseSecurityService.ValidHeaderConfiguration("X-Key", "X-Signature", "X-Time", "X-Nonce", "X-Response")); Assert.False(LicenseSecurityService.ValidHeaderConfiguration("X-Key", "X-Key", "X-Time", "X-Nonce", "X-Response")); Assert.False(LicenseSecurityService.ValidHeaderConfiguration("Authorization", "X-Signature", "X-Time", "X-Nonce", "X-Response")); }
}
