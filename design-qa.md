# Design QA — Digital Products v1.1.1

## Evidence

- Reference: `/var/folders/70/d8rtf1ds5f14stgc4yv_4c600000gn/T/codex-clipboard-cf127f32-214d-4a3d-96d1-a74ca95cc58c.png`
- Implementation: `.design-qa/storefront-v1.1.1-desktop-final.png`
- Combined comparison: `.design-qa/reference-vs-implementation.png`
- Desktop viewport: 1488 × 1058; public storefront; two published demo products.
- Cart evidence: `.design-qa/cart-v1.1.1-desktop.png` and `.design-qa/cart-v1.1.1-mobile.png`.

## Visual comparison

- Matches the selected blue promotion strip, compact white navigation, wide soft-gray hero, product filters/cards, and structured navy footer.
- Uses the merchant-configured wordmark, content, colors, and catalog data; configured logo, hero, and product images override the bundled defaults.
- Added real generated hero/download/license artwork sized for the visible slots. No placeholder or CSS-drawn product artwork remains.
- Refined the hero headline after side-by-side review to match the reference's compact two-line desktop scale.
- The live catalog contains two products rather than the three shown in the reference, so the grid correctly leaves the remaining column empty instead of inventing catalog data.

## Functional and responsive QA

- Verified live header, filters, account/cart navigation, CTA anchors, add-to-cart forms, footer links, and embedded artwork endpoints.
- Added one download and one license, then verified the two-line cart at desktop width.
- Confirmed quantity input, Update button, and total price occupy separate grid tracks with no overlap.
- Confirmed DOM layout at 390px has no horizontal overflow (`scrollWidth === innerWidth`) for both storefront and cart.
- Shared form overlay disables submit controls, marks the form busy, and blocks duplicate submits; `pageshow` safely restores controls.

## Automated and deployment checks

- Digital Products: 22/22 tests passed with Release Razor compilation.
- Event Tickets: 11/11 tests passed with Release Razor compilation.
- Live BTCPay logs loaded both plugins as version 1.1.1.0.

final result: passed
