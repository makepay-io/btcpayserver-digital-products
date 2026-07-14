# Design QA — Digital Products 1.4.1

## Grounding

- Storefront direction: the approved modern ecommerce homepage supplied by the user, including the shared header, restrained blue system, product-card grid, and dark MakePay/BTCPay footer.
- Editor source: the supplied Canapes page-builder screens and the implementation at `/Users/jozefvojtas/Documents/GitHub/canapes-middle-east/apps/web/app/[locale]/admin/page-builder`.
- Editor comparison: the Canapes `Screenshot 2026-07-13 at 5.37.10 PM.png` and Digital Products `Screenshot 2026-07-13 at 6.08.02 PM.png` were opened together at the same 1728 × 894 CSS-pixel viewport for direct visual review.
- Media-product direction: the supplied SendOwl photos reference, adapted to BTCPay's existing admin components and the established MakePay storefront system.

## Visual comparison and inspection

- The fullscreen editor preserves the source builder's three-part hierarchy: ordered layers at left, a large interactive page canvas in the middle, and a focused inspector at right.
- Toolbar grouping, page selection, saved/dirty state, device preview, undo/redo, add actions, and selected-section outlines follow the source interaction density without copying its brand styling.
- Canvas, inspector, and layer-list scrolling are independent; section changes update the preview in place and preserve canvas and inspector positions.
- The live production editor exposes the six non-duplicated, product-aware categories: Downloads, Ebooks, Music & Audio, Video, Photos & Art, and Licenses. Empty categories are suppressed on the public storefront.
- Storefront and detail cards retain the approved contained-media treatment so fine cover lines remain visible. Generated ebook, audio, video, and art covers use real raster assets, not placeholder drawings.
- The enforced footer uses the `✦ MakePay` lockup and balanced BTCPay mark with working project backlinks; merchant settings cannot remove or rewrite the MakePay attribution.
- Public product detail views were inspected for each media family: book reader controls and fallback, native audio demo, native video trailer, watermarked art gallery, delivery metadata, pricing, and cart action.
- The admin product editor follows BTCPay form patterns while adding clear type selection, storage/delivery controls, media metadata, preview uploads, and client-side downscale/watermark preparation for art previews.
- Public and admin layouts include explicit mobile breakpoints, reflowing navigation, single-column product/detail layouts, full-width purchase actions, scroll-safe editor panels, and non-overlapping media controls.

## Interaction and regression verification

- Live editor section selection updates without a page refresh and preserves the editing context.
- Save with an intentionally empty optional catalog subtitle was exercised against production. It returned `Digital product settings saved.` with no validation error; the demo subtitle was then restored.
- Storefront, every seeded product detail page, and category filters return HTTP 200.
- PDF, audio, video, and art preview assets return the expected MIME types. Audio/video range requests return HTTP 206 with valid `Content-Range`; invalid ranges return HTTP 416.
- Protected delivery enforces paid status, token expiry, optional IP lock, delivery mode, download limits, and restart-aware range accounting. A byte-zero range restart consumes a new allowance while later byte offsets may finish the counted transfer.
- Bundled same-origin PDF.js runtime and worker assets load successfully without a third-party CDN dependency.
- MakePay Digital Products 1.4.1 starts successfully in BTCPay Server 2.3.5 and serves the simulated six-product catalog.

## Automated verification

- Release build and Razor compilation: passed.
- Automated tests: 82 passed, 0 failed.
- JavaScript syntax and `git diff --check`: passed.
- Only observed build warning is the upstream BTCPay Server MailKit 4.8.0 NU1902 advisory; it is not introduced by this plugin change.

final result: passed

# Design QA — Digital Products 1.6.0

## Grounding

- Admin palette source: `/var/folders/70/d8rtf1ds5f14stgc4yv_4c600000gn/T/TemporaryItems/NSIRD_screencaptureui_oCAVkU/Screenshot 2026-07-14 at 12.33.09 PM.png`.
- Editor layout source: `/var/folders/70/d8rtf1ds5f14stgc4yv_4c600000gn/T/TemporaryItems/NSIRD_screencaptureui_aFXqZY/Screenshot 2026-07-14 at 12.30.43 PM.png`.
- Upload-control source: `/var/folders/70/d8rtf1ds5f14stgc4yv_4c600000gn/T/codex-clipboard-b2d066bb-671e-4568-9079-c47269ff6e3b.png`.
- Empty-validation source: `/var/folders/70/d8rtf1ds5f14stgc4yv_4c600000gn/T/TemporaryItems/NSIRD_screencaptureui_mQj9rt/Screenshot 2026-07-14 at 12.34.51 PM.png`.
- Sidebar-icon source: `/var/folders/70/d8rtf1ds5f14stgc4yv_4c600000gn/T/TemporaryItems/NSIRD_screencaptureui_lSS4Rz/Screenshot 2026-07-14 at 12.57.28 PM.png`.
- Rendered implementation evidence:
  - `.design-qa/admin-unified-2048x1056.png`
  - `.design-qa/license-keys-unified-2048x1056.png`
  - `.design-qa/editor-btcpay-dark-2048x1056.png`
  - `.design-qa/editor-upload-2048x1056.png`
  - `.design-qa/product-editor-no-empty-error-2048x1056.png`
  - `.design-qa/storefront-cart-popover-2048x1056.png`
  - `.design-qa/storefront-mobile-390x844.png`
  - `.design-qa/storefront-mobile-cart-390x844.png`
- Desktop viewport: 2048 × 1056 CSS pixels. Mobile resilience viewport: 390 × 844 CSS pixels.
- States: authenticated BTCPay dark dashboard; Products tab; License keys tab; fullscreen editor open with Header selected; product editor initial GET; public storefront with added-to-cart popover open.

## Full-view comparison evidence

- The BTCPay dashboard source and `admin-unified-2048x1056.png` were opened together in one comparison input. The unified Products/License keys view follows BTCPay's dark surfaces, restrained borders, native green emphasis, dashboard density, and sidebar hierarchy.
- The page-builder layout source, BTCPay palette source, and `editor-btcpay-dark-2048x1056.png` were opened together in one comparison input. The implementation preserves the three-pane editor hierarchy while mapping its chrome to adaptive BTCPay tokens.
- Desktop and mobile storefront captures were inspected for stable hero geometry, cart feedback placement, readable product cards, and horizontal-overflow resilience.

## Focused region comparison evidence

- The upload-control source and `editor-upload-2048x1056.png` were compared together. Logo, favicon, and hero-image inputs use a polished dashed upload card with explicit action, filename feedback, help copy, and focus treatment instead of browser-native chrome.
- The empty-validation source and `product-editor-no-empty-error-2048x1056.png` were compared together. The initial product editor no longer renders an empty red validation summary.
- The sidebar-icon source and the sidebar region of `admin-unified-2048x1056.png` were compared together. Digital Products uses BTCPay's native `nav-products` icon and Event Tickets uses the native QR-code icon with matching alignment, scale, and current-color behavior.
- No additional focused crop was needed for the unified license view because its text, status controls, and table spacing are clearly readable in `license-keys-unified-2048x1056.png`.

## Required fidelity surfaces

- Fonts and typography: native BTCPay font stacks, weights, line heights, labels, table text, and button hierarchy remain consistent with the surrounding dashboard; no clipping or unintended truncation was observed.
- Spacing and layout rhythm: dashboard panels, tabs, tables, editor columns, inspector groups, upload cards, and public cart feedback retain clear alignment, stable gaps, and consistent radii at desktop and mobile widths.
- Colors and visual tokens: admin and editor chrome use adaptive BTCPay background, border, text, muted, success, danger, and primary-green tokens. Merchant storefront colors remain isolated to the preview and public shop.
- Image quality and asset fidelity: existing merchant raster artwork remains sharp and contained. No replacement CSS art, placeholder imagery, emoji, or handcrafted SVG artwork was introduced.
- Copy and content: Products and License keys use one clear information architecture; upload guidance, validation feedback, cart confirmation, and license actions use concise task-oriented copy.
- Icons: navigation and editor controls use the BTCPay icon sprite/component system and remain optically aligned with adjacent native entries.
- Accessibility and states: upload controls retain labels and focus styling; the cart popover exposes status text through an `aria-live` region, supports close/continue actions, and remains usable at 390 px; selected tabs and editor layers expose their active state.

## Comparison history

- Iteration 1 — P1 palette and information-architecture drift: the editor used a separate light visual system and license administration lived on a disconnected route. Adaptive BTCPay tokens, unified Products/License keys tabs, canonical license routing, and legacy redirect handling were added. Post-fix evidence: `admin-unified-2048x1056.png`, `license-keys-unified-2048x1056.png`, and `editor-btcpay-dark-2048x1056.png`.
- Iteration 2 — P2 browser-native uploads: live-editor file inputs did not match the product editor or reference quality. Reusable upload cards, filename feedback, immediate preview handling, and safe upload stashing were added. Post-fix evidence: `editor-upload-2048x1056.png`.
- Iteration 3 — P2 empty error state: a validation-summary container rendered on an initial product-editor GET without a message. Rendering is now conditional on actual ModelState errors. Post-fix evidence: `product-editor-no-empty-error-2048x1056.png` and zero matching visible `.alert-danger` elements.
- Iteration 4 — P1 storefront interruption: Add to cart performed a full navigation and interrupted browsing. The storefront and product detail page now submit in the background, update cart count, emit analytics once, and show a dismissible cart popover. Post-fix evidence: `storefront-cart-popover-2048x1056.png`.
- Iteration 5 — P2 layout instability: hero slides with different content heights moved the catalog vertically. Slides now share one overlapping grid track and a stable measured height. Browser measurements before and after advancing the carousel were both 339.265625 CSS pixels with a zero-pixel bottom-edge delta.
- Iteration 6 — P2 missing native navigation affordance: plugin entries appeared without icons. Native BTCPay Digital Products and Event Tickets icons were added. Post-fix evidence: sidebar comparison in `admin-unified-2048x1056.png`.

## Interaction and regression verification

- Products and License keys switch within the unified Digital Products administration surface.
- The legacy `/plugins/{storeId}/license-manager` GET redirects to `/plugins/{storeId}/digital-downloads?section=licenses`.
- Non-default license statuses submit correctly; missing and invalid statuses are rejected instead of silently defaulting to Active.
- Live-editor section changes update in place without a full refresh; the selected header inspector appeared without closing or navigating the editor.
- Logo, favicon, and hero uploads retain only the selected non-empty file field when the editor rerenders its inspector.
- Product-editor initial GET has no empty validation banner or visible danger summary.
- Add to cart kept the storefront URL, updated the header cart count, opened the confirmation popover, and exposed working Go to cart and Continue browsing actions. The same public-page URL remained before and after submission.
- Carousel Next changed the active slide from `Digital goods, delivered directly` to `A new digital product` without changing the measured hero height or catalog position.
- The 390 × 844 storefront had a 390-pixel document width, no horizontal overflow, and a fully visible 362-pixel-wide cart popover with both actions on screen.
- Browser console warnings and errors were checked after the admin, editor, product editor, storefront, cart-popover, and carousel flows. No plugin-origin errors remained; the only historical entries were BTCPay Blazor reconnect messages from the candidate container restart.

## Findings

- No actionable P0, P1, or P2 fidelity, behavior, responsive, or accessibility findings remain.
- P3 follow-up: consider a dedicated tablet-width capture in a future polish pass; current desktop and mobile evidence covers the release's changed surfaces.

## Automated verification

- Release build and Razor compilation: passed.
- Automated tests: 141 passed, 0 failed.
- Cart runtime and inline editor JavaScript syntax checks: passed.
- `git diff --check`: passed.
- Only observed build warning is the upstream BTCPay Server MailKit 4.8.0 NU1902 advisory; it is not introduced by this plugin release.

final result: passed
