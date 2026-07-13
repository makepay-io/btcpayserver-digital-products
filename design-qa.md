# Design QA

## Visual source

- Selected source: the deployed MakePay Event Tickets split-layout public shop.
- Implemented target: the deployed MakePay Digital Products public shop and fullscreen editor.
- Compared at the same desktop canvas using `.design-qa/source-event-tickets.png` and `.design-qa/after-storefront-desktop.png`.

## Comparison

- Preserves the source's full-height blue brand panel, large uppercase hero typography, compact uppercase eyebrow labels, white commerce canvas, restrained borders, and high-contrast primary actions.
- Adapts the ticket selector into a unified download/license catalog without losing the source hierarchy or spacing rhythm.
- Product cards, cart, passwordless sign-in, payment handoff, completion, and library share one consistent type, color, spacing, and component system.
- Fullscreen editor mirrors the Event Tickets editor interaction model and previews Shop, Cart, Sign in, Payment, Success, and Library in desktop and mobile modes.
- At the 390px editor viewport, the split hero stacks, products and library collapse to one column, and actions remain readable and reachable.

## Functional QA

- Added one download and one license to the same encrypted cart.
- Confirmed authenticated cart checkout creates a server-side BTCPay invoice.
- Confirmed `/modal/btcpay.js` opens the official BTCPay checkout modal with the new invoice.
- Marked the test invoice settled and confirmed automatic redirect to the completion page.
- Confirmed fulfillment issued both a protected download and generated license key from the same checkout.
- Confirmed the customer library shows both products and the paid purchase-history row.
- Downloaded the protected sample and confirmed usage changed from 0/3 to 1/3.
- Confirmed the temporary authenticated QA route was removed from the final deployed build (HTTP 404).

## Automated checks

- Release Razor compilation passed.
- 22/22 unit tests passed.
- Final deployment loaded as plugin version 1.1.0.0 on BTCPay Server 2.3.5.

## Result

passed
