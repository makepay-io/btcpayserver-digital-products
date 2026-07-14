# MakePay Digital Products for BTCPay Server

A self-hosted BTCPay Server plugin for selling digital media and generated software licenses from one branded storefront. Customers can combine products in one cart, sign in with a one-time email code, pay through BTCPay's JavaScript checkout modal, and return to a private purchase library.

Version 1.4.2 adds an in-product custom-domain guide with the live canonical storefront URL, safe DNS/TLS instructions for BTCPay Docker operators, and an explicit explanation of the current clean-path limitation.

Version 1.4.1 adds a per-store favicon URL and managed local favicon upload. The configured icon is emitted consistently across the shop, product, cart, sign-in, payment, confirmation, purchase-library, protected-delivery, and legacy license pages; an empty setting emits no custom favicon tag.

Version 1.4 adds consent-aware Google Tag Manager and direct Google Analytics 4 commerce analytics to the media storefront introduced in version 1.3. The existing file-download and license data model remains compatible:

- **File download** — protected delivery for archives, source files, templates, and other downloadable assets.
- **PDF / ebook** — optional public sample and a book-style browser powered by the bundled PDF.js viewer.
- **Music & audio** — optional public demo, protected playback, and configurable stream/download delivery.
- **Video content** — optional trailer, protected playback, and configurable stream/download delivery for courses, tutorials, and footage.
- **Photos & art** — public preview galleries with derived, downscaled, optionally watermarked preview assets while originals remain behind purchase access.
- **Software license** — generated serials, activation limits, signed verification APIs, and lifecycle management.

Existing per-store settings, products, orders, issued licenses, and API credentials retain their storage keys and migrate in place. Older products and fulfillment snapshots that do not contain a media type continue as file downloads.

## Storefront and administration

- A professional BTCPay-native product editor with media-type selection, delivery controls, metadata, cover art, preview/demo management, fulfillment configuration, and publication state.
- An editable storefront with responsive product cards, product detail pages, type-aware previews, custom categories, hero slides, logo, favicon, colors, typography, and page content.
- Empty categories are hidden. Existing custom categories and explicit product selections continue to work.
- Passwordless customer access using encrypted, one-time, expiring email codes and encrypted store-scoped sessions.
- A private customer library with protected downloads and streams, recoverable license keys, activation state, and checkout history.
- A server-created invoice opened through BTCPay's official `/modal/btcpay.js` integration, with payment-state polling and product unlock.
- Enforced MakePay.io attribution and backlink in the public footer.

## Custom domains

The storefront can be reached through a branded hostname after the domain and BTCPay reverse proxy are configured. The route available today remains store-scoped, for example:

```text
https://shop.example.com/stores/<storeId>/downloads
```

For an official Docker deployment:

1. Configure an A/AAAA record to the BTCPay server, or a CNAME to its public hostname, and wait for DNS to resolve.
2. Confirm ports 80 and 443 reach BTCPay.
3. As the server administrator, set `BTCPAY_ADDITIONAL_HOSTS` to the **complete comma-separated list**, retaining all existing additional hostnames, then rerun setup:

   ```bash
   # Replace both placeholders. Run as root from the btcpayserver-docker directory.
   export BTCPAY_ADDITIONAL_HOSTS="<your-new-host>,<all-existing-additional-hosts>"
   . ./btcpay-setup.sh -i
   ```

A CNAME alone is insufficient: the receiving reverse proxy must accept the hostname and provide a valid TLS certificate. Verify DNS before adding the hostname, because an unresolved or incorrect additional hostname can prevent Let's Encrypt renewal for every configured hostname, including the primary BTCPay domain.

`BTCPAY_ADDITIONAL_HOSTS` aliases the entire BTCPay Server, not only this store. Other BTCPay pages and store-scoped public routes remain reachable through that hostname, so it must not be treated as domain-to-store isolation.

DNS and `BTCPAY_ADDITIONAL_HOSTS` do not remove `/stores/<storeId>` from the plugin route. Digital Products is not currently registered as a BTCPay App, so Server Settings → Policies cannot map it to a clean root route. A clean `https://shop.example.com/downloads` URL requires a host-to-store short-route implementation or an external reverse proxy that correctly rewrites every related storefront, asset, checkout, callback, and protected-delivery route. If the apex domain already hosts another website, its proxy or CDN must own that path routing; a dedicated subdomain is recommended.

See the official BTCPay documentation for [Docker additional hosts](https://docs.btcpayserver.org/Docker/#environment-variables), [mapping domains to BTCPay Apps](https://docs.btcpayserver.org/FAQ/Apps/#how-to-map-a-domain-name-to-an-app), and [external reverse proxy and TLS configuration](https://docs.btcpayserver.org/FAQ/Deployment/#can-i-use-an-existing-nginx-server-as-a-reverse-proxy-with-ssl-termination).

## Analytics and privacy

- Choose one provider per store: Google Tag Manager or direct Google Analytics 4. Provider IDs are validated and normalized before save so the same events are not reported twice by the plugin.
- GA4 commerce events cover `view_item_list`, `select_item`, `view_item`, `add_to_cart`, `remove_from_cart`, `view_cart`, `begin_checkout`, `add_payment_info`, and `purchase`. Purchase uses best-effort browser deduplication plus a stable one-way transaction ID so GA4 can deduplicate repeated delivery.
- Every commerce payload contains normalized currency, value, and item data. Buyer emails, license keys, delivery tokens, checkout access tokens, and raw order/checkout identifiers are never included. Purchase transaction IDs are stable one-way analytics identifiers.
- Optional consent prevents Google scripts and event collection before acceptance. Rejected visitors have no private replay queue, and the preference can be changed from the persistent **Analytics preferences** control. Revoking consent reloads into a Google-script-free state. Browser Do Not Track can also be enforced.
- Direct GA4 disables automatic page views and sends only a sanitized origin-plus-path location. Dynamic checkout/order capability segments are masked, referrers receive the same treatment, and the local `dataLayer` follows the identical contract. When Do Not Track is enabled, collection is disabled in Google and local data-layer modes.

GTM containers are merchant-controlled JavaScript and can still read the current browser URL. Protected checkout, receipt, and sign-in routes can contain access-token or email parameters. Configure GTM tags to trigger from the plugin's `page_view` event and map the supplied `page_location` and `page_path` values. Do not use an automatic **All Pages** page-view trigger or GTM's built-in **Page URL** variable on these routes.

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
3. Install `BTCPayServer.Plugins.MakePay.DigitalProducts` version 1.4.1 or newer.
4. Restart BTCPay Server. Existing products default to **File download**, existing License Manager storage remains readable, and the new empty media categories stay hidden until products are added.

Use private S3 buckets and dedicated read-only credentials. The optional IP lock is useful against casual link sharing but can inconvenience mobile users whose network address changes. Download and license delivery defaults to settled/confirmed invoices; enabling delivery at Processing accepts additional payment risk.

Created by [MakePay.io](https://makepay.io) — accept 90+ currencies in a decentralized way in BTCPay Server.
