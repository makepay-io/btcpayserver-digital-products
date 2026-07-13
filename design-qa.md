# Design QA — Digital Products 1.2.0

## Grounding

- Storefront direction: the approved modern ecommerce homepage screenshot supplied in this task.
- Footer wordmark reference: `Screenshot 2026-07-13 at 6.36.30 PM.png`, showing the exact source-grounded `✦ MakePay` lockup.
- Footer spacing defect: `Screenshot 2026-07-13 at 6.36.58 PM.png`, showing the oversized BTCPay container and undersized legacy MakePay icon that had to be removed.
- Editor interaction reference: the supplied Canapes page-builder screens and the local page-builder implementation named by the user.

## Comparison and inspection

- Replaced the legacy boxed logo row with a single aligned flex row: BTCPay is constrained to 126 × 32 px and the MakePay wordmark uses the requested `✦ MakePay` lockup at a balanced 1.2 rem size.
- Removed editable MakePay attribution controls from settings and from the live-editor inspector. The required copy and backlink are rendered independently of merchant settings.
- Product images use `object-fit: contain` in cards and the detail view so thin source artwork is preserved instead of being cropped or softened by cover scaling.
- Added a responsive product-detail composition with breadcrumbs, large contained product media, delivery metadata, quantity handling, related products, shared navigation, and the same enforced footer.
- The editor now previews the product-detail page, preserves canvas/inspector scroll on selection changes, updates the iframe body in place, restores editor state, supports undo/redo, and removes duplicate stored category slugs from the editing surface.
- Desktop, tablet, and mobile rules were checked for title wrapping, footer reflow, product columns, purchase controls, and minimum interactive target sizing.

## Runtime verification

- Plugin build: passed with zero errors.
- Automated tests: 27 passed, 0 failed.
- Deployed plugin startup: BTCPay loaded `BTCPayServer.Plugins.MakePay.DigitalProducts - 1.2.0.0` without plugin errors.
- Public storefront HTTP render: 200; enforced MakePay wordmark and backlink present; editable attribution labels absent.
- Public product-detail HTTP render: 200; add-to-cart, related products, balanced footer marks, and enforced backlink present.

final result: passed
