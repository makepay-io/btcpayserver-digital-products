(() => {
    'use strict';

    if (window.makePayCart) return;

    const formSelector = 'form[data-makepay-cart-form="true"]';
    const triggerSelector = '[data-makepay-cart-trigger]';
    const countSelector = '[data-makepay-cart-count]';
    const popoverId = 'makepay-cart-popover';
    let popover;
    let dismissTimer;
    let interacted = false;
    let lastTrigger;

    function ensurePopover() {
        if (popover) return popover;
        popover = document.createElement('section');
        popover.id = popoverId;
        popover.className = 'dp-cart-popover';
        popover.hidden = true;
        popover.setAttribute('role', 'dialog');
        popover.setAttribute('aria-labelledby', `${popoverId}-title`);
        popover.setAttribute('aria-describedby', `${popoverId}-message`);
        popover.innerHTML = `
            <button class="dp-cart-popover-close" type="button" data-makepay-cart-close aria-label="Close cart update">&times;</button>
            <p class="dp-cart-popover-eyebrow">Your cart</p>
            <h2 id="${popoverId}-title" data-makepay-cart-title>Added to cart</h2>
            <p id="${popoverId}-message" data-makepay-cart-message role="status" aria-live="polite" aria-atomic="true"></p>
            <div class="dp-cart-popover-actions">
                <a class="dp-primary" data-makepay-cart-link href="#">Go to cart</a>
                <button class="dp-secondary" type="button" data-makepay-cart-continue>Continue browsing</button>
            </div>`;
        document.body.appendChild(popover);

        const markInteraction = () => {
            interacted = true;
            clearTimeout(dismissTimer);
        };
        popover.addEventListener('pointerenter', markInteraction);
        popover.addEventListener('focusin', markInteraction);
        popover.querySelector('[data-makepay-cart-close]').addEventListener('click', dismiss);
        popover.querySelector('[data-makepay-cart-continue]').addEventListener('click', dismiss);
        return popover;
    }

    function cartUrl(value) {
        const fallback = document.querySelector(triggerSelector)?.href || '/';
        try {
            const candidate = new URL(value || fallback, window.location.href);
            return candidate.origin === window.location.origin ? candidate.href : fallback;
        } catch {
            return fallback;
        }
    }

    function positionPopover() {
        if (!popover || popover.hidden || window.matchMedia('(max-width: 640px)').matches) return;
        const trigger = lastTrigger || document.querySelector(triggerSelector);
        if (!trigger) return;
        const bounds = trigger.getBoundingClientRect();
        popover.style.top = `${Math.max(12, bounds.bottom + 10)}px`;
        popover.style.right = `${Math.max(16, window.innerWidth - bounds.right)}px`;
    }

    function updateCount(value) {
        const count = Math.max(0, Math.trunc(Number(value) || 0));
        document.querySelectorAll(countSelector).forEach(element => {
            element.textContent = String(count);
        });
        return count;
    }

    function dismiss() {
        clearTimeout(dismissTimer);
        if (!popover || popover.hidden) return;
        popover.classList.remove('is-visible');
        popover.hidden = true;
        document.querySelectorAll(triggerSelector).forEach(trigger => trigger.setAttribute('aria-expanded', 'false'));
    }

    function show(payload, submittedForm) {
        const element = ensurePopover();
        const item = payload?.item || {};
        const count = updateCount(payload?.cartCount);
        const added = Math.max(0, Math.trunc(Number(item.addedQuantity) || 0));
        const name = String(item.name || 'Product');
        element.querySelector('[data-makepay-cart-title]').textContent = added > 0
            ? `${name} added to your cart`
            : `${name} is already in your cart`;
        const message = element.querySelector('[data-makepay-cart-message]');
        const summary = count === 1
            ? 'Your cart now contains 1 item.'
            : `Your cart now contains ${count} items.`;
        message.textContent = '';
        element.querySelector('[data-makepay-cart-link]').href = cartUrl(payload?.cartUrl);

        lastTrigger = submittedForm.closest('body')?.querySelector(triggerSelector) || document.querySelector(triggerSelector);
        document.querySelectorAll(triggerSelector).forEach(trigger => {
            trigger.setAttribute('aria-controls', popoverId);
            trigger.setAttribute('aria-expanded', 'true');
        });
        interacted = false;
        element.hidden = false;
        element.classList.add('is-visible');
        positionPopover();
        window.requestAnimationFrame(() => {
            message.textContent = summary;
        });
        clearTimeout(dismissTimer);
        dismissTimer = window.setTimeout(() => {
            if (!interacted) dismiss();
        }, 6500);
    }

    function setBusy(form, busy) {
        if (busy) form.setAttribute('aria-busy', 'true');
        else form.removeAttribute('aria-busy');
        form.querySelectorAll('button[type="submit"],input[type="submit"]').forEach(control => {
            control.disabled = busy;
        });
    }

    document.addEventListener('submit', async event => {
        const form = event.target instanceof HTMLFormElement ? event.target : null;
        if (!form?.matches(formSelector)) return;
        if (form.dataset.makepayCartSubmitting === 'true') {
            event.preventDefault();
            return;
        }

        event.preventDefault();
        form.dataset.makepayCartSubmitting = 'true';
        setBusy(form, true);
        try {
            const response = await fetch(form.action, {
                method: (form.method || 'post').toUpperCase(),
                body: new FormData(form),
                credentials: 'same-origin',
                headers: {
                    Accept: 'application/json',
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });
            if (!response.ok) throw new Error(`Cart request failed with ${response.status}`);
            const payload = await response.json();
            if (!Number.isFinite(Number(payload?.cartCount))) throw new Error('Cart response was incomplete');
            show(payload, form);
            setBusy(form, false);
            delete form.dataset.makepayCartSubmitting;
        } catch {
            // This does not emit another submit event, so analytics records add_to_cart once.
            setBusy(form, false);
            delete form.dataset.makepayCartSubmitting;
            HTMLFormElement.prototype.submit.call(form);
        }
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') dismiss();
    });
    document.addEventListener('pointerdown', event => {
        if (!popover || popover.hidden || popover.contains(event.target)) return;
        if (event.target instanceof Element && event.target.closest(triggerSelector)) return;
        dismiss();
    });
    window.addEventListener('resize', positionPopover);
    window.addEventListener('pageshow', () => {
        document.querySelectorAll(formSelector).forEach(form => {
            delete form.dataset.makepayCartSubmitting;
            setBusy(form, false);
        });
    });

    window.makePayCart = Object.freeze({ dismiss, updateCount });
})();
