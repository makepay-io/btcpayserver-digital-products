# MakePay Digital Products for BTCPay Server

A self-hosted BTCPay Server plugin for selling digital media and generated software licenses from one branded storefront. Customers can combine products in one cart, sign in with a one-time email code, pay through BTCPay's JavaScript checkout modal, and return to a private purchase library.

Version 1.3 expands digital products into five purpose-built media types while retaining the existing file-download and license data model:

- **File download** — protected delivery for archives, source files, templates, and other downloadable assets.
- **PDF / ebook** — optional public sample and a book-style browser powered by the bundled PDF.js viewer.
- **Music & audio** — optional public demo, protected playback, and configurable stream/download delivery.
- **Video content** — optional trailer, protected playback, and configurable stream/download delivery for courses, tutorials, and footage.
- **Photos & art** — public preview galleries with derived, downscaled, optionally watermarked preview assets while originals remain behind purchase access.
- **Software license** — generated serials, activation limits, signed verification APIs, and lifecycle management.

Existing per-store settings, products, orders, issued licenses, and API credentials retain their storage keys and migrate in place. Older products and fulfillment snapshots that do not contain a media type continue as file downloads.

## Storefront and administration

- A professional BTCPay-native product editor with media-type selection, delivery controls, metadata, cover art, preview/demo management, fulfillment configuration, and publication state.
- An editable storefront with responsive product cards, product detail pages, type-aware previews, custom categories, hero slides, logo, colors, typography, and page content.
- Empty categories are hidden. Existing custom categories and explicit product selections continue to work.
- Passwordless customer access using encrypted, one-time, expiring email codes and encrypted store-scoped sessions.
- A private customer library with protected downloads and streams, recoverable license keys, activation state, and checkout history.
- A server-created invoice opened through BTCPay's official `/modal/btcpay.js` integration, with payment-state polling and product unlock.
- Enforced MakePay.io attribution and backlink in the public footer.

## Protected media delivery

- Original products can use local protected uploads, private S3-compatible objects, or authenticated custom source URLs.
- Local originals are scoped to the owning store and product. Public preview files are stored separately under a store/product/preview boundary.
- Product fulfillment data is snapshotted at checkout so later catalog edits do not redirect an existing purchase to a different source.
- Files stream through BTCPay Server; origin paths, credentials, and private object URLs are not exposed to the customer.
- Single-range HTTP delivery supports seekable audio/video playback without consuming an additional download for every range request.
- Cryptographically random delivery tokens, SHA-256 token hashes, encrypted recoverable tokens, expiration, download limits, revocation, and optional first-IP locking.
- Remote-origin SSRF protection with public-address validation, DNS checks, disabled redirects, and validated custom authentication headers.
- Consolidated purchase delivery and passwordless login through the BTCPay store SMTP configuration with editable HTML templates.

Public previews are intentionally public. A photo watermark discourages casual reuse but is not a substitute for keeping the original private. Likewise, tokenized audio/video streaming controls access and supports expiry and revocation, but browser playback is not DRM and cannot prevent a determined customer from recording content they can play.

Preview and demo uploads are limited to 95 MB so requests remain below the common 100 MB BTCPay reverse-proxy limit. Large master files should use a private S3-compatible object or protected custom origin. PDF previews use the bundled PDF.js assets from the same BTCPay origin; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## License management

- Configurable serial formats with `{A:n}`, `{N:n}`, `{X:n}`, `{HEX:n}`, and `{YEAR}` tokens.
- Encrypted recoverable license keys plus normalized hashes for lookup.
- Manual issuance and invoice-based fulfillment with activation count and validity controls.
- Verify, activate, deactivate, and heartbeat endpoints at `/api/v1/stores/{storeId}/licenses/{action}`.
- Configurable custom `X-` headers, HMAC-SHA256 request/response signatures, constant-time comparison, timestamp windows, nonce replay protection, rate limiting, activation limits, and bounded audit history.

## Requirements

- BTCPay Server 2.3.5 or newer.
- .NET 8 SDK to build.
- BTCPay store email settings if email delivery or passwordless customer access is enabled.

## Build and test

```bash
git submodule update --init --recursive
dotnet test tests/BTCPayServer.Plugins.MakePay.DigitalProducts.Tests/BTCPayServer.Plugins.MakePay.DigitalProducts.Tests.csproj -c Release
dotnet publish src/BTCPayServer.Plugins.MakePay.DigitalProducts/BTCPayServer.Plugins.MakePay.DigitalProducts.csproj -c Release
```

Install the published plugin folder in the BTCPay Server plugin directory, restart BTCPay Server, then open **Store → Integrations → Digital Products**.

## Upgrade from an earlier or standalone plugin

1. Back up the BTCPay data directory.
2. If present, remove the standalone `BTCPayServer.Plugins.MakePay.LicenseManager` plugin folder.
3. Install `BTCPayServer.Plugins.MakePay.DigitalProducts` version 1.3.0 or newer.
4. Restart BTCPay Server. Existing products default to **File download**, existing License Manager storage remains readable, and the new empty media categories stay hidden until products are added.

Use private S3 buckets and dedicated read-only credentials. The optional IP lock is useful against casual link sharing but can inconvenience mobile users whose network address changes. Download and license delivery defaults to settled/confirmed invoices; enabling delivery at Processing accepts additional payment risk.

Created by [MakePay.io](https://makepay.io) — accept 90+ currencies in a decentralized way in BTCPay Server.
