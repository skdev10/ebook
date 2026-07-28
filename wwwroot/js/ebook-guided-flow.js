/**
 * Guided book creation flow (Writer → Formatting → Cover → Publish).
 * Active only when the user starts from AI Writer / Write new.
 * Direct dashboard/sidebar entry to Formatting, Cover, or Publish clears the flag
 * so Next / Continue buttons stay hidden.
 */
(function (global) {
    'use strict';

    var KEY = 'ebook_guided_flow';

    function mark() {
        try { sessionStorage.setItem(KEY, '1'); } catch (e) { /* ignore */ }
    }

    function clear() {
        try { sessionStorage.removeItem(KEY); } catch (e) { /* ignore */ }
    }

    function isActive() {
        try {
            var q = new URLSearchParams(global.location.search || '');
            if (q.get('guided') === '0' || q.get('entry') === 'direct') {
                clear();
                return false;
            }
            if (q.get('guided') === '1') {
                mark();
                return true;
            }
            return sessionStorage.getItem(KEY) === '1';
        } catch (e) {
            return false;
        }
    }

    function appendGuided(url) {
        if (!url || typeof url !== 'string') return url;
        if (/([?&])guided=/.test(url)) return url;
        return url + (url.indexOf('?') >= 0 ? '&' : '?') + 'guided=1';
    }

    function applyUi() {
        var on = isActive();
        try {
            document.documentElement.setAttribute('data-ebook-guided-flow', on ? '1' : '0');
        } catch (e0) { /* ignore */ }
        document.querySelectorAll('[data-guided-flow-only]').forEach(function (el) {
            if (on) {
                el.classList.remove('hidden');
                el.style.removeProperty('display');
                el.setAttribute('aria-hidden', 'false');
            } else {
                el.classList.add('hidden');
                el.style.display = 'none';
                el.setAttribute('aria-hidden', 'true');
            }
        });
    }

    document.addEventListener('click', function (e) {
        var t = e.target;
        if (!t || !t.closest) return;
        if (t.closest('[data-entry-direct]')) clear();
        if (t.closest('[data-start-guided-flow]')) mark();
    }, true);

    global.EbookGuidedFlow = {
        KEY: KEY,
        mark: mark,
        clear: clear,
        isActive: isActive,
        appendGuided: appendGuided,
        applyUi: applyUi
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', applyUi);
    } else {
        applyUi();
    }
})(window);
