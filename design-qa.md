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
