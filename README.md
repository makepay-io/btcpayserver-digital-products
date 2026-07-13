# MakePay Digital Products for BTCPay Server

One self-hosted BTCPay Server plugin for selling protected digital downloads and generated software licenses. Customers browse one branded storefront, pay a normal BTCPay invoice, and receive either an expiring download link or a protected license key after payment.

Version 1.0 combines MakePay Digital Downloads and MakePay License Manager into one installable plugin. Existing per-store settings, products, orders, issued licenses, and API credentials use their original storage keys and migrate in place.

## Unified storefront and administration

- One **Digital Products** BTCPay integration entry with Downloads and License keys sections.
- One responsive, branded storefront with logo, accent color, descriptions, covers, pricing, and separate download/license catalogs.
- MakePay.io promotion area for enabling decentralized acceptance of 90+ currencies.

## Protected downloads

- Local protected uploads, S3-compatible private objects, or authenticated custom source URLs.
- Files stream through BTCPay Server; origin paths, credentials, and object URLs are never exposed.
- Cryptographically random delivery tokens, SHA-256 token hashes, encrypted recoverable tokens, expiration, download limits, revocation, and optional first-IP locking.
- Remote-origin SSRF protection with HTTPS/public-address validation, DNS checks, disabled redirects, and validated custom authentication headers.
- Delivery through the BTCPay store SMTP configuration with editable HTML and placeholders.

## License management

- Configurable serial formats with `{A:n}`, `{N:n}`, `{X:n}`, `{HEX:n}`, and `{YEAR}` tokens.
- Encrypted recoverable license keys plus normalized hashes for lookup.
- Manual issuance and invoice-based fulfillment with activation count and validity controls.
- Verify, activate, deactivate, and heartbeat endpoints at `/api/v1/stores/{storeId}/licenses/{action}`.
- Configurable custom `X-` headers, HMAC-SHA256 request/response signatures, constant-time comparison, timestamp windows, nonce replay protection, rate limiting, activation limits, and bounded audit history.

## Requirements

- BTCPay Server 2.3.5 or newer.
- .NET 8 SDK to build.
- BTCPay store email settings if email delivery is enabled.

## Build and test

```bash
git submodule update --init --recursive
dotnet test tests/BTCPayServer.Plugins.MakePay.DigitalProducts.Tests/BTCPayServer.Plugins.MakePay.DigitalProducts.Tests.csproj -c Release
dotnet publish src/BTCPayServer.Plugins.MakePay.DigitalProducts/BTCPayServer.Plugins.MakePay.DigitalProducts.csproj -c Release
```

Install the published plugin folder in the BTCPay Server plugin directory, restart BTCPay Server, then open **Store → Integrations → Digital Products**.

## Upgrade from the standalone plugins

1. Back up the BTCPay data directory.
2. Remove the standalone `BTCPayServer.Plugins.MakePay.LicenseManager` plugin folder.
3. Install `BTCPayServer.Plugins.MakePay.DigitalProducts` version 1.0.0 or newer.
4. Restart BTCPay Server. The combined plugin reads the existing License Manager store settings and data without a migration.

Use private S3 buckets and dedicated read-only credentials. The optional IP lock is useful against casual link sharing but can inconvenience mobile users whose network address changes. Download and license delivery defaults to settled/confirmed invoices; enabling delivery at Processing accepts additional payment risk.

Created by [MakePay.io](https://makepay.io) — accept 90+ currencies in a decentralized way in BTCPay Server.
