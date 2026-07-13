(() => {
    'use strict';

    const runtime = document.currentScript;
    if (!runtime || window.makePayAnalytics?.version) return;

    const provider = runtime.dataset.provider || 'disabled';
    const containerId = (runtime.dataset.gtmContainerId || '').toUpperCase();
    const measurementId = (runtime.dataset.gaMeasurementId || '').toUpperCase();
    const storeId = runtime.dataset.storeId || '';
    const pageType = runtime.dataset.pageType || 'public';
    const plugin = runtime.dataset.plugin || 'digital_products';
    const requireConsent = runtime.dataset.requireConsent === 'true';
    const respectDoNotTrack = runtime.dataset.respectDnt !== 'false';
    const hasGtm = provider === 'google_tag_manager' && /^GTM-[A-Z0-9]+$/.test(containerId);
    const hasGa = provider === 'google_analytics' && /^G-[A-Z0-9]+$/.test(measurementId);
    const configured = hasGtm || hasGa;
    const dnt = respectDoNotTrack && (navigator.doNotTrack === '1' || window.doNotTrack === '1');
    const consentKey = `makepay.analytics.consent.v1.${plugin}.${storeId}`;
    const oncePrefix = `makepay.analytics.once.v1.${plugin}.${storeId}.`;
    function sanitizePagePath(value) {
        const path = typeof value === 'string' && value.startsWith('/') ? value : '/';
        return path
            .replace(/(\/downloads\/checkout\/)[^/]+/gi, '$1:checkout_id')
            .replace(/(\/downloads\/purchase\/)[^/]+/gi, '$1:checkout_id')
            .replace(/(\/downloads\/order\/)[^/]+/gi, '$1:order_id');
    }

    const safePagePath = sanitizePagePath(window.location.pathname);
    const safePageLocation = `${window.location.origin}${safePagePath}`;
    const safePageReferrer = (() => {
        if (!document.referrer) return '';
        try {
            const source = new URL(document.referrer);
            return `${source.origin}${sanitizePagePath(source.pathname)}`;
        } catch {
            return '';
        }
    })();
    const memoryOnce = new Set();
    const dataLayer = window.dataLayer = window.dataLayer || [];
    let providerStarted = false;
    let domBound = false;
    let consentChoice = readStorage(consentKey);

    function googleConsentState(granted) {
        return {
            analytics_storage: granted ? 'granted' : 'denied',
            ad_storage: 'denied',
            ad_user_data: 'denied',
            ad_personalization: 'denied'
        };
    }

    function updateGoogleConsent(command, granted) {
        if (!configured) return;
        window.gtag = window.gtag || function () { dataLayer.push(arguments); };
        window.gtag('consent', command, googleConsentState(granted));
    }

    function readStorage(key) {
        try { return window.localStorage.getItem(key); } catch { return null; }
    }

    function writeStorage(key, value) {
        try { window.localStorage.setItem(key, value); return true; } catch { return false; }
    }

    function copyNonce(target) {
        const source = document.querySelector('script[nonce]');
        const nonce = source?.nonce || source?.getAttribute('nonce');
        if (nonce) target.setAttribute('nonce', nonce);
    }

    function injectScript(source) {
        const script = document.createElement('script');
        script.async = true;
        script.src = source;
        copyNonce(script);
        (document.head || document.documentElement).appendChild(script);
    }

    function directEvent(name, payload) {
        if (!hasGa || !providerStarted) return;
        window.gtag('event', name, {
            ...payload,
            page_location: safePageLocation,
            page_path: safePagePath,
            page_referrer: safePageReferrer
        });
    }

    function mayTrack() {
        if (dnt) return false;
        if (!configured) return true;
        return !requireConsent || consentChoice === 'granted';
    }

    function loadProvider() {
        if (providerStarted || !configured || dnt || (requireConsent && consentChoice !== 'granted')) return;
        providerStarted = true;
        window.gtag = window.gtag || function () { dataLayer.push(arguments); };
        window.gtag('set', {
            page_location: safePageLocation,
            page_path: safePagePath,
            page_referrer: safePageReferrer
        });

        if (hasGtm) {
            dataLayer.push({ 'gtm.start': Date.now(), event: 'gtm.js' });
            injectScript(`https://www.googletagmanager.com/gtm.js?id=${encodeURIComponent(containerId)}`);
            return;
        }

        window.gtag('js', new Date());
        window.gtag('config', measurementId, {
            anonymize_ip: true,
            allow_google_signals: false,
            allow_ad_personalization_signals: false,
            send_page_view: false,
            transport_type: 'beacon'
        });
        injectScript(`https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(measurementId)}`);
        sendDirectPageView();
    }

    function sendDirectPageView() {
        if (!hasGa || !providerStarted) return;
        window.gtag('event', 'page_view', {
            page_title: document.title,
            page_location: safePageLocation,
            page_path: safePagePath,
            page_referrer: safePageReferrer
        });
    }

    function pushLayer(value) {
        dataLayer.push(value);
    }

    function normalizePayload(payload) {
        const normalized = payload && typeof payload === 'object' ? structuredCloneSafe(payload) : {};
        if (normalized.currency) normalized.currency = String(normalized.currency).trim().toUpperCase();
        if (Array.isArray(normalized.items)) {
            normalized.items = normalized.items.map(item => ({
                ...item,
                price: finiteNumber(item.price),
                quantity: Math.max(1, Math.trunc(finiteNumber(item.quantity) || 1))
            }));
        } else {
            normalized.items = [];
        }
        normalized.value = finiteNumber(normalized.value);
        return normalized;
    }

    function structuredCloneSafe(value) {
        try { return JSON.parse(JSON.stringify(value)); } catch { return {}; }
    }

    function finiteNumber(value) {
        const number = Number(value);
        return Number.isFinite(number) ? number : 0;
    }

    function track(name, payload) {
        if (!name || typeof name !== 'string' || !mayTrack()) return false;
        const ecommerce = normalizePayload(payload);
        const reset = { ecommerce: null };
        const event = {
            event: name,
            page_location: safePageLocation,
            page_path: safePagePath,
            page_referrer: safePageReferrer,
            makepay: {
                plugin,
                store_id: storeId,
                page_type: pageType,
                schema_version: '1'
            },
            ecommerce
        };
        pushLayer(reset);
        pushLayer(event);
        directEvent(name, ecommerce);
        return true;
    }

    function trackOnce(name, uniqueId, payload) {
        if (!uniqueId) return track(name, payload);
        if (!mayTrack()) return false;
        const key = `${oncePrefix}${encodeURIComponent(name)}.${encodeURIComponent(String(uniqueId))}`;
        if (memoryOnce.has(key) || readStorage(key) === '1') return false;
        memoryOnce.add(key);
        writeStorage(key, '1');
        return track(name, payload);
    }

    function setConsent(granted) {
        const newlyGranted = granted && consentChoice !== 'granted';
        const reloadWithoutProvider = !granted && providerStarted;
        consentChoice = granted ? 'granted' : 'denied';
        writeStorage(consentKey, consentChoice);
        updateGoogleConsent('update', granted && !dnt);
        if (granted && !dnt) {
            if (newlyGranted) pushPageContext();
            const providerWasStarted = providerStarted;
            loadProvider();
            if (newlyGranted && providerWasStarted) sendDirectPageView();
        }
        if (granted && !dnt) processLoadEvents();
        document.querySelectorAll('[data-makepay-analytics-consent]').forEach(element => element.hidden = true);
        window.dispatchEvent(new CustomEvent('makepay:analytics-consent', { detail: { granted } }));
        if (reloadWithoutProvider) window.location.reload();
    }

    function parsePayload(element) {
        const source = element.dataset.makepayAnalyticsPayload || element.textContent || '{}';
        try { return JSON.parse(source); } catch { return {}; }
    }

    function adjustQuantity(payload, quantity) {
        const next = normalizePayload(payload);
        if (!next.items.length) return next;
        next.items[0].quantity = Math.max(1, Math.trunc(quantity || 1));
        next.value = next.items.reduce((sum, item) => sum + finiteNumber(item.price) * item.quantity, 0);
        return next;
    }

    function processLoadEvents() {
        document.querySelectorAll('[data-makepay-analytics-load]').forEach(element => {
            if (element.dataset.makepayAnalyticsProcessed === 'true') return;
            const name = element.dataset.makepayAnalyticsLoad;
            const once = element.dataset.makepayAnalyticsOnce;
            const tracked = once ? trackOnce(name, once, parsePayload(element)) : track(name, parsePayload(element));
            if (tracked) element.dataset.makepayAnalyticsProcessed = 'true';
        });
    }

    function bindDom() {
        processLoadEvents();
        if (domBound) return;
        domBound = true;

        document.addEventListener('click', event => {
            const target = event.target instanceof Element
                ? event.target.closest('[data-makepay-analytics-click]')
                : null;
            if (!target) return;
            track(target.dataset.makepayAnalyticsClick, parsePayload(target));
        }, { capture: true });

        document.addEventListener('submit', event => {
            const form = event.target instanceof Element ? event.target.closest('form') : null;
            if (!form) return;

            if (form.dataset.makepayAnalyticsCartUpdate === 'true') {
                const current = Math.max(0, Math.trunc(finiteNumber(form.dataset.currentQuantity)));
                const requested = Math.max(0, Math.trunc(finiteNumber(new FormData(form).get('quantity'))));
                const difference = requested - current;
                if (difference !== 0) {
                    const payload = adjustQuantity(parsePayload(form), Math.abs(difference));
                    track(difference > 0 ? 'add_to_cart' : 'remove_from_cart', payload);
                }
                return;
            }

            const name = form.dataset.makepayAnalyticsForm;
            if (!name) return;
            const requestedValue = new FormData(form).get('quantity');
            const payload = requestedValue === null
                ? parsePayload(form)
                : adjustQuantity(parsePayload(form), Math.max(1, Math.trunc(finiteNumber(requestedValue)) || 1));
            track(name, payload);
        }, { capture: true });

        const consent = document.querySelector('[data-makepay-analytics-consent]');
        const preferences = document.querySelector('[data-makepay-consent-preferences]');
        if (consent) {
            if (configured && requireConsent && !dnt && consentChoice === null) consent.hidden = false;
            consent.querySelector('[data-makepay-consent-accept]')?.addEventListener('click', () => setConsent(true));
            consent.querySelector('[data-makepay-consent-reject]')?.addEventListener('click', () => setConsent(false));
        }
        if (preferences && configured && requireConsent && !dnt) {
            preferences.hidden = false;
            preferences.addEventListener('click', () => {
                if (consent) consent.hidden = false;
            });
        }
    }

    window.makePayAnalytics = Object.freeze({
        version: '1.0.0',
        track,
        trackOnce,
        purchaseOnce: (transactionId, payload) => trackOnce('purchase', transactionId, payload),
        setConsent,
        context: Object.freeze({ plugin, storeId, pageType })
    });

    function pushPageContext() {
        pushLayer({
            event: 'makepay_page_context',
            page_location: safePageLocation,
            page_path: safePagePath,
            page_referrer: safePageReferrer,
            makepay: {
                plugin,
                store_id: storeId,
                page_type: pageType,
                schema_version: '1'
            }
        });
        if (!hasGa) {
            pushLayer({
                event: 'page_view',
                page_title: document.title,
                page_location: safePageLocation,
                page_path: safePagePath,
                page_referrer: safePageReferrer,
                makepay: {
                    plugin,
                    store_id: storeId,
                    page_type: pageType,
                    schema_version: '1'
                }
            });
        }
    }

    updateGoogleConsent('default', !dnt && (!requireConsent || consentChoice === 'granted'));
    if (mayTrack()) {
        pushPageContext();
        loadProvider();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', bindDom, { once: true });
    else bindDom();
})();
