#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BTCPayServer.Plugins.MakePay.LicenseManager.Services;

public sealed partial class LicenseKeyGenerator
{
    private const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string AlphaNumeric = Letters + Digits;
    private const string Hex = "0123456789ABCDEF";

    public string Generate(string pattern)
    {
        Validate(pattern);
        var result = TokenRegex().Replace(pattern, match =>
        {
            if (match.Groups[1].Value == "YEAR") return DateTimeOffset.UtcNow.Year.ToString();
            var kind = match.Groups[2].Value;
            var length = int.Parse(match.Groups[3].Value);
            var alphabet = kind switch { "A" => Letters, "N" => Digits, "X" => AlphaNumeric, "HEX" => Hex, _ => throw new FormatException("Unknown key token.") };
            var builder = new StringBuilder(length);
            for (var i = 0; i < length; i++) builder.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
            return builder.ToString();
        });
        if (result.Length > 200) throw new FormatException("Generated keys cannot exceed 200 characters.");
        return result;
    }

    public void Validate(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 200) throw new FormatException("Key pattern must be between 1 and 200 characters.");
        var stripped = TokenRegex().Replace(pattern, "");
        if (stripped.Contains('{') || stripped.Contains('}')) throw new FormatException("Invalid placeholder. Use {A:n}, {N:n}, {X:n}, {HEX:n}, or {YEAR}.");
        foreach (Match match in TokenRegex().Matches(pattern))
            if (match.Groups[3].Success && (int.Parse(match.Groups[3].Value) is < 1 or > 64)) throw new FormatException("Placeholder lengths must be between 1 and 64.");
    }

    [GeneratedRegex("\\{(?:(YEAR)|(?:(A|N|X|HEX):(\\d{1,3})))\\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
